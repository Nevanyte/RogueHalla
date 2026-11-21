//! Viewer-scoped action offers and authoritative turn-in range checks.
use super::interactions::{has_spare_weapon, CONTACT_RANGE};
use super::*;
use QuestId::*;

pub(crate) fn choices(
    k: &MatchStatic,
    w: &World,
    owner: PlayerId,
    o: &Offer,
    quest: crate::quest_view::QuestHandle,
    out: &mut Vec<crate::quest_view::QuestAction>,
) {
    let Some(a) = attempt(w, owner, o.id) else {
        return;
    };
    let Some(id) = kind(o) else { return };
    let Some(player) = w.player(owner).filter(|_| owner_allowed(w, owner)) else {
        return;
    };
    if a.completed || a.pending.is_some() {
        return;
    }
    let mut push = |action, target, at, label: &str| {
        out.push(crate::quest_view::QuestAction {
            quest,
            action,
            target,
            at: Option::<Vec2>::into_iter(at).collect(),
            label: label.into(),
        });
    };
    push(1, 0, None, "Abandon");
    if a.ready
        || w.rt.gates_locked
        || crate::status_runtime::action_blocked(&player.statuses, w.server_time)
    {
        return;
    }
    let pos = player.pos();
    let close = |at: Vec2| near(k, w, pos, at, CONTACT_RANGE);
    let at = |i| objective_for_owner(w, owner, o.id, i).filter(|&at| close(at));
    let items = match id {
        SmugglersRun if !a.carrying => &["Take the parcel"][..],
        TwoBanners if a.branch == Some(1) && !a.carrying => &["Take the rival banner"],
        TheBurningCrown if !a.carrying => &["Take the Burning Crown"],
        EmberWalk => &["Collect ember token"; 3],
        LetHimCook => &[
            "Take the apple",
            "Take the wildberries",
            "Take the turtle stew",
        ],
        TuberEats => &["Deliver the rancid potato"],
        Letters => &["Deliver the letter"],
        EvenSkeletonsGetCold => &["Deliver the coat"],
        LostBrother => &["Take the lost relic"],
        ACruelJoke => &["Scare the giver"; 3],
        FeedOrHunt => &[
            "Feed the forager",
            "Gather feed",
            "Gather feed",
            "Gather feed",
        ],
        WhispersInTheBrush => &["Collect hidden token"; 3],
        TradeUp => &["Barter with the trader"; 3],
        _ => &[],
    };
    for (i, label) in items.iter().enumerate() {
        let i = i as u8;
        let enabled = match id {
            FeedOrHunt if i == 0 => {
                (1..=3).any(|j| a.bits & (1 << j) != 0 && a.bits & (1 << (j + 3)) == 0)
            }
            FeedOrHunt => a.bits & (1 << i) == 0,
            _ => true,
        };
        if enabled {
            if let Some(position) = at(i) {
                push(3, i, Some(position), label);
            }
        }
    }
    if id == TwoBanners && a.branch.is_none() && close(o.at) {
        push(2, 0, Some(o.at), "Carry the Gallows banner");
        push(2, 1, Some(o.at), "Carry the rival banner");
    }
    if id == StayAWhile && a.meditation.is_none() && close(o.at) {
        push(6, 0, Some(o.at), "Meditate");
    }
    let end = endpoint(w, o, id, a);
    if id == TradeUp && a.progress > 0 && close(end) {
        push(4, 0, Some(end), "Cash out");
    }
    if id == TheBargain && close(end) {
        for (i, give) in BARGAIN_STAT_NAMES.iter().enumerate() {
            if bargain_stacks(player, i as u8) < BARGAIN_COST {
                continue;
            }
            for (j, receive) in BARGAIN_STAT_NAMES
                .iter()
                .enumerate()
                .filter(|(j, _)| *j != i)
            {
                push(
                    5,
                    i as u8 * BARGAIN_STAT_COUNT + j as u8,
                    Some(end),
                    &format!("Trade {give} for {receive}"),
                );
            }
        }
        if has_spare_weapon(w, player) {
            for (i, stat) in BARGAIN_STAT_NAMES.into_iter().enumerate() {
                push(
                    7,
                    i as u8,
                    Some(end),
                    &format!("Trade spare weapon for {stat}"),
                );
            }
        }
    }
}

/// Recheck at execution because a delivered choice can arrive after movement or a new lock.
pub(crate) fn at_turn_in(k: &MatchStatic, w: &World, owner: PlayerId, offer_id: u32) -> bool {
    if !owner_allowed(w, owner) || w.rt.gates_locked {
        return false;
    }
    let Some(a) = attempt(w, owner, offer_id) else {
        return false;
    };
    let Some(o) = offer(w, offer_id) else {
        return false;
    };
    let Some(id) = kind(o) else {
        return false;
    };
    w.player(owner).is_some_and(|p| {
        a.pending.is_some()
            && !crate::status_runtime::action_blocked(&p.statuses, w.server_time)
            && near(k, w, p.pos(), endpoint(w, o, id, a), QUEST_TURN_IN_RANGE)
    })
}
