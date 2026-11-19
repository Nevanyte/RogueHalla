//! World events that advance a particular player’s quest attempt.
use super::*;
use crate::hooks::HookArgs;
use crate::mob_catalog::MobTypeId;
use QuestId::*;

fn offer_for(w: &World, id: QuestId) -> Option<u32> {
    w.quests
        .as_ref()?
        .offers
        .iter()
        .find(|o| kind(o) == Some(id))
        .map(|o| o.id)
}

fn award(w: &mut World, owner: PlayerId, quest: QuestId) {
    if !owner_allowed(w, owner) {
        return;
    }
    let Some(offer) = offer_for(w, quest) else {
        return;
    };
    let now = w.server_time;
    let locked = w.rt.gates_locked;
    if let Some(a) = board_mut(w).and_then(|b| b.get_mut(owner, offer)) {
        if !a.completed && !a.ready && (locked || a.deadline == 0. || a.deadline > now) {
            a.achieve(quest.definition().objective_count);
        }
    }
}

pub(crate) fn bind_target(w: &mut World, offer: u32, actor: PlayerId) {
    let Some(kind) = super::offer(w, offer).and_then(kind) else {
        return;
    };
    if !matches!(
        kind,
        TheWarden | ABoneToPick | EliteSlayer | RimeHunt | FeedOrHunt
    ) {
        return;
    }
    let Some(b) = board_mut(w) else { return };
    if b.targets.iter().any(|t| t.actor == actor) {
        return;
    }
    if kind != RimeHunt || !b.targets.iter().any(|t| t.offer == offer && t.alive) {
        b.targets.retain(|t| t.offer != offer || t.alive);
    }
    b.targets.push(Target {
        offer,
        actor,
        alive: true,
    });
}

pub(crate) fn on_target_killed(w: &mut World, killer: Option<PlayerId>, victim: PlayerId) {
    let Some(offer) = board(w).and_then(|b| {
        b.targets
            .iter()
            .find(|t| t.actor == victim && t.alive)
            .map(|t| t.offer)
    }) else {
        return;
    };
    let Some(id) = super::offer(w, offer).and_then(kind) else {
        return;
    };
    let eligible = killer.filter(|&owner| owner_allowed(w, owner));
    let b = board_mut(w).unwrap();
    b.targets
        .iter_mut()
        .find(|t| t.actor == victim)
        .unwrap()
        .alive = false;
    match id {
        TheWarden | ABoneToPick | EliteSlayer => {
            if let Some(owner) = eligible {
                award(w, owner, id);
            }
        }
        RimeHunt => {
            if let Some(owner) = eligible {
                if let Some(a) = b.get_mut(owner, offer) {
                    a.bits = 1;
                }
            }
        }
        _ => {}
    }
}

pub(crate) fn on_mob_killed(hx: &mut Hx, args: &HookArgs) {
    // Every real Scorch elite qualifies for Elite Slayer. Only concrete
    // bindings qualify for the Warden and the Barrow Archer.
    if let &HookArgs::MobKilled {
        killer,
        elite: true,
        zone: crate::FactionId::Fire,
        ..
    } = args
    {
        award(hx.w, killer, EliteSlayer);
    }
}

pub(crate) fn on_camp_cleared(hx: &mut Hx, args: &HookArgs) {
    let &HookArgs::CampCleared { camp } = args else {
        return;
    };
    if !targets::is_ice_camp(hx.k, camp as usize) {
        return;
    }
    let Some(offer) = offer_for(hx.w, RimeHunt) else {
        return;
    };
    let total = board(hx.w).map_or(0, |b| b.attempts.len());
    for i in 0..total {
        let a = &board(hx.w).unwrap().attempts[i];
        if a.offer == offer && a.bits != 0 {
            let owner = a.owner;
            board_mut(hx.w).unwrap().attempts[i].bits = 0;
            award(hx.w, owner, RimeHunt);
        }
    }
}

pub(crate) fn on_bounty_kill(w: &mut World, by: PlayerId, victim: PlayerId, bounty: u32) {
    if bounty > 0 && by != victim && w.player(victim).is_some() {
        award(w, by, BountyHunter);
    }
}

pub(crate) fn on_player_killed(hx: &mut Hx, args: &HookArgs) {
    let &HookArgs::PlayerKilled {
        by,
        victim,
        outcome: crate::results::Outcome::Died,
    } = args
    else {
        return;
    };
    if by != victim && board(hx.w).is_some_and(|b| b.bounty == Some(victim)) {
        award(hx.w, by, BountyHunter);
        if let Some(b) = board_mut(hx.w) {
            b.bounty = None;
        }
    }
}

pub(super) fn update_bounty(w: &mut World) {
    if offer_for(w, BountyHunter).is_none() {
        return;
    }
    let mut humans = w.players.iter().filter(|p| p.standing() && !p.seat.bot);
    let primary = humans.next().map(|p| p.id);
    let solo = primary.is_some() && humans.next().is_none();
    let active_bounty = w
        .players
        .iter()
        .any(|p| p.standing() && Some(p.id) != primary && p.credit.bounty > 0);
    let mut target = None;
    if solo && !active_bounty {
        target = board(w)
            .and_then(|b| b.bounty)
            .filter(|&id| w.player(id).is_some_and(|p| p.standing() && p.seat.bot));
        if target.is_none() {
            target = w
                .players
                .iter()
                .find(|p| p.standing() && p.seat.bot)
                .map(|p| p.id);
        }
    }
    board_mut(w).unwrap().bounty = target;
}

fn enter_lock(w: &mut World) {
    let letters = offer_for(w, Letters);
    let meditate = offer_for(w, StayAWhile);
    let Some(b) = board_mut(w) else { return };
    if b.locked {
        return;
    }
    b.locked = true;
    b.lock = b.lock.saturating_add(1);
    b.attempts
        .retain(|a| Some(a.offer) != letters || a.completed || a.pending.is_some());
    for a in b
        .attempts
        .iter_mut()
        .filter(|a| Some(a.offer) == meditate && !a.ready)
    {
        a.meditation = None;
        a.dwell_ms = 0.;
    }
}

pub(crate) fn on_teleported(hx: &mut Hx, _args: &HookArgs) {
    if hx.w.rt.gates_locked {
        enter_lock(hx.w);
    }
}

pub(super) fn update_lock(w: &mut World) {
    if w.rt.gates_locked {
        enter_lock(w);
        return;
    }
    if !board(w).is_some_and(|b| b.locked) {
        return;
    }
    let gatecrasher = offer_for(w, Gatecrasher);
    let lock = board(w).unwrap().lock;
    board_mut(w).unwrap().locked = false;
    let total = board(w).unwrap().attempts.len();
    for i in 0..total {
        let a = &board(w).unwrap().attempts[i];
        if Some(a.offer) != gatecrasher || a.completed || a.ready {
            continue;
        }
        let achieved = owner_allowed(w, a.owner) && a.lock == lock && a.damaged[1].is_some();
        let a = &mut board_mut(w).unwrap().attempts[i];
        if achieved {
            a.achieve(2);
        } else {
            a.progress = 0;
        }
        a.damaged = [None; 2];
    }
}

pub(crate) fn on_hit_landed(hx: &mut Hx, args: &HookArgs) {
    let HookArgs::HitLanded { entry, amount } = args else {
        return;
    };
    if *amount <= 0. || board(hx.w).is_none() {
        return;
    }
    let meditate = offer_for(hx.w, StayAWhile);
    if let Some(a) = meditate.and_then(|offer| board_mut(hx.w)?.get_mut(entry.victim, offer)) {
        a.meditation = None;
        if !a.ready {
            a.dwell_ms = 0.;
        }
    }
    if !hx.w.rt.gates_locked || !hx.w.players.contains(entry.victim) {
        return;
    }
    let Some(owner) = entry
        .credit_id()
        .and_then(|by| hx.w.credited_player(by))
        .filter(|&by| by != entry.victim && owner_allowed(hx.w, by))
    else {
        return;
    };
    let Some(offer) = offer_for(hx.w, Gatecrasher) else {
        return;
    };
    enter_lock(hx.w);
    let b = board_mut(hx.w).unwrap();
    let lock = b.lock;
    let Some(a) = b.get_mut(owner, offer) else {
        return;
    };
    if a.completed || a.ready {
        return;
    }
    if a.lock != lock {
        a.damaged = [None; 2];
        a.lock = lock;
    }
    if a.damaged.contains(&Some(entry.victim)) {
        return;
    }
    if let Some(empty) = a.damaged.iter_mut().find(|s| s.is_none()) {
        *empty = Some(entry.victim);
        a.progress += 1;
    }
}

pub(crate) fn on_drop_body(hx: &mut Hx, args: &HookArgs) {
    let &HookArgs::DropBody { victim, .. } = args else {
        return;
    };
    let at = hx.w.player(victim).map(|p| p.pos());
    let crown_offer = hx.w.quests.as_ref().and_then(|q| {
        q.offers
            .iter()
            .find(|o| kind(o) == Some(QuestId::TheBurningCrown))
            .map(|o| o.id)
    });
    let Some(b) = board_mut(hx.w) else { return };
    if b.crown.carrier == Some(victim) {
        b.crown.carrier = None;
        b.crown.ground = at;
    }
    b.attempts.retain(|a| {
        a.owner != victim || a.completed || (a.pending.is_some() && Some(a.offer) != crown_offer)
    });
}

pub(super) fn burn_crown(cx: &mut Cx) {
    use crate::systems::ledger::Entry;
    let Some(carrier) = board(cx.w).and_then(|b| b.crown.carrier) else {
        return;
    };
    let Some(p) = cx.w.player(carrier).filter(|p| p.standing()) else {
        return;
    };
    let amount = p.vitals.max_hp * CROWN_BURN_RATE;
    let b = board_mut(cx.w).unwrap();
    let before = b.crown.elapsed_ms;
    b.crown.elapsed_ms += TICK_MS;
    if (before / 1000.) as u32 == (b.crown.elapsed_ms / 1000.) as u32 {
        return;
    }
    cx.w.deferred.defer_hit(Entry {
        terrain: true,
        dot: false,
        ability: None,
        arena: false,
        source: None,
        critical: false,
        critical_mult: None,
        sig: None,
        execute: false,
        victim: carrier,
        by: None,
        packet: crate::state::Packet {
            raw: amount,
            phys: 0.,
            magic: 0.,
            true_: 1.,
        },
        weapon: None,
        stagger: false,
        projectile: None,
        scale: 1.,
        derived: false,
        passive: false,
    });
}

pub(crate) const BARGAIN_STAT_COUNT: u8 = 9;
pub(crate) const BARGAIN_COST: u64 = 20;
pub(super) const BARGAIN_STAT_NAMES: [&str; BARGAIN_STAT_COUNT as usize] = [
    "damage",
    "move speed",
    "max HP",
    "ability power",
    "armor",
    "shield",
    "attack damage",
    "attack speed",
    "lifesteal",
];

pub(crate) fn bargain_stat(index: u8) -> Option<MobTypeId> {
    use MobTypeId::*;
    [
        EmberImp,
        CinderHound,
        MagmaBrute,
        FlameCaller,
        Bailiff,
        ChainWarden,
        Pardoner,
        PlagueRat,
        RotMonk,
    ]
    .get(index as usize)
    .copied()
}

fn bargain_family(index: u8) -> impl Iterator<Item = MobTypeId> {
    use crate::modifiers::farm::{Farm, DEFINITION};
    let reward = bargain_stat(index).and_then(Farm::reward_of);
    DEFINITION
        .rows
        .iter()
        .filter(move |row| reward.is_some_and(|chosen| chosen.stat == row.stat))
        .map(|row| row.mob)
}

pub(crate) fn bargain_stacks(player: &crate::state::Player, index: u8) -> u64 {
    bargain_family(index)
        .map(|mob| player.farm.count(mob))
        .sum()
}

pub(crate) fn bargain_take(player: &mut crate::state::Player, index: u8, mut stacks: u64) -> bool {
    if bargain_stacks(player, index) < stacks {
        return false;
    }
    for mob in bargain_family(index) {
        let count = player.farm.count(mob);
        let take = count.min(stacks);
        player.farm.set(mob, count - take);
        stacks -= take;
        if stacks == 0 {
            break;
        }
    }
    true
}
