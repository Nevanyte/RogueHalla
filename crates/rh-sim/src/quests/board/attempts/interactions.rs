//! Validated actions at givers, items and delivery contacts.
use super::*;
use QuestId::*;

pub(super) const CONTACT_RANGE: f64 = QUEST_TURN_IN_RANGE;
const FORT_RANGE: f64 = 72.;
const BRUSH_REVEAL_RANGE: f64 = 112.;
const GIVER_FLEE_MS: f64 = 60_000.;

pub(super) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for value in [CONTACT_RANGE, FORT_RANGE, BRUSH_REVEAL_RANGE, GIVER_FLEE_MS] {
        word(value.to_bits());
    }
}

pub(crate) fn visible_giver(w: &World, owner: PlayerId, giver_offer: u32) -> bool {
    board(w).is_none_or(|b| {
        !b.fled_givers
            .iter()
            .any(|g| g.owner == owner && g.giver_offer == giver_offer && w.server_time < g.until)
    })
}

pub(super) fn prank_contact(w: &World, starting_offer: u32, index: u8) -> Option<(u32, Vec2)> {
    if index >= 3 {
        return None;
    }
    let offers = &w.quests.as_ref()?.offers;
    let mut cursor = offer(w, starting_offer)?.at;
    let mut seen = [starting_offer, 0, 0, 0];
    for i in 0..=index as usize {
        let next = offers
            .iter()
            .filter(|o| !seen.contains(&o.id) && o.giver.is_some() && kind(o).is_some())
            .min_by(|a, b| distance(cursor, a.at).total_cmp(&distance(cursor, b.at)))?;
        seen[i + 1] = next.id;
        cursor = next.at;
    }
    Some((seen[index as usize + 1], cursor))
}

fn crown_ground(state: &BoardState, o: &Offer) -> Option<Vec2> {
    if state.crown.carrier.is_some() {
        return None;
    }
    state.crown.ground.or_else(|| o.tokens.first().copied())
}

pub(super) fn abandon(w: &mut World, owner: PlayerId, offer_id: u32) {
    let is_crown = offer(w, offer_id).and_then(kind) == Some(TheBurningCrown);
    let pos = w.player(owner).map(|p| p.pos());
    let Some(b) = board_mut(w) else { return };
    if b.abandon(owner, offer_id) && is_crown && b.crown.carrier == Some(owner) {
        b.crown.carrier = None;
        b.crown.ground = pos;
    }
}

pub(crate) fn interact(cx: &mut Cx, owner: PlayerId, offer_id: u32, action: u8, target: u8) {
    if !owner_allowed(cx.w, owner) {
        return;
    }
    if action == 1 {
        abandon(cx.w, owner, offer_id);
        return;
    }
    if cx.w.rt.gates_locked {
        return;
    }
    let player = cx.w.player(owner).unwrap();
    if crate::status_runtime::action_blocked(&player.statuses, cx.w.server_time) {
        return;
    }
    let pos = player.pos();
    let Some(o) = offer(cx.w, offer_id) else {
        return;
    };
    let Some(id) = kind(o) else { return };
    let Some(a) = attempt(cx.w, owner, offer_id) else {
        return;
    };
    if a.completed
        || a.ready
        || a.pending.is_some()
        || (a.deadline > 0. && a.deadline <= cx.w.server_time)
    {
        return;
    }
    let at = if id == TradeUp {
        trader_at(cx.w, o, target)
    } else {
        o.tokens.get(target as usize).copied()
    };
    let at_contact = at.is_some_and(|p| near(cx.k, cx.w, pos, p, CONTACT_RANGE));

    match (action, id) {
        (2, TwoBanners)
            if a.branch.is_none() && target < 2 && near(cx.k, cx.w, pos, o.at, CONTACT_RANGE) =>
        {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.branch = Some(target);
            a.carrying = target == 0;
        }
        (3, Letters | TuberEats | EvenSkeletonsGetCold) if target == 0 && at_contact => {
            board_mut(cx.w)
                .unwrap()
                .get_mut(owner, offer_id)
                .unwrap()
                .achieve(1);
        }
        (3, TwoBanners) if target == 0 && at_contact && a.branch == Some(1) => {
            board_mut(cx.w)
                .unwrap()
                .get_mut(owner, offer_id)
                .unwrap()
                .carrying = true;
        }
        (3, SmugglersRun | LostBrother) if target == 0 && at_contact => {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.carrying = true;
            if id == LostBrother {
                a.achieve(1);
            }
        }
        (3, TheBurningCrown) if target == 0 => {
            let at = crown_ground(board(cx.w).unwrap(), o);
            if at.is_some_and(|p| near(cx.k, cx.w, pos, p, CONTACT_RANGE)) {
                let b = board_mut(cx.w).unwrap();
                b.crown.carrier = Some(owner);
                b.crown.ground = None;
                b.get_mut(owner, offer_id).unwrap().carrying = true;
            }
        }
        (3, EmberWalk | LetHimCook | WhispersInTheBrush) if target < 3 && at_contact => {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            if a.claim_anchor(target) && a.progress == 3 {
                a.achieve(3);
            }
        }
        (3, ACruelJoke) if target < 3 => {
            let Some((contact, at)) = prank_contact(cx.w, offer_id, target) else {
                return;
            };
            if near(cx.k, cx.w, pos, at, CONTACT_RANGE) {
                let until = cx.w.server_time + GIVER_FLEE_MS;
                let b = board_mut(cx.w).unwrap();
                let a = b.get_mut(owner, offer_id).unwrap();
                if a.claim_anchor(target) {
                    if a.progress == 3 {
                        a.achieve(3);
                    }
                    if let Some(g) = b
                        .fled_givers
                        .iter_mut()
                        .find(|g| g.owner == owner && g.giver_offer == contact)
                    {
                        g.until = until;
                    } else {
                        b.fled_givers.push(FledGiver {
                            owner,
                            giver_offer: contact,
                            until,
                        });
                    }
                }
            }
        }
        (3, FeedOrHunt) if (1..=3).contains(&target) && at_contact => {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.bits |= 1 << target;
        }
        (3, FeedOrHunt) if target == 0 => {
            let b = board(cx.w).unwrap();
            let creature = b.targets.iter().find(|t| t.offer == offer_id && t.alive);
            let at = creature
                .and_then(|t| cx.w.mobs.get(t.actor))
                .map(|m| m.core.pos());
            let item = (1..=3).find(|&i| a.bits & (1 << i) != 0 && a.bits & (1 << (i + 3)) == 0);
            if let Some(item) =
                item.filter(|_| at.is_some_and(|p| near(cx.k, cx.w, pos, p, CONTACT_RANGE)))
            {
                let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
                a.claim_anchor(item + 3);
                if a.progress == 3 {
                    a.achieve(3);
                }
            }
        }
        (3, TradeUp) if at_contact && target == a.progress && target < 3 => {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.progress += 1;
            if a.progress == 3 {
                a.reward.points = 3;
                a.achieve(3);
            }
        }
        (4, TradeUp)
            if a.progress > 0 && near(cx.k, cx.w, pos, endpoint(cx.w, o, id, a), CONTACT_RANGE) =>
        {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.reward.points = a.progress;
            a.achieve(a.progress);
        }
        (5 | 7, TheBargain) if near(cx.k, cx.w, pos, endpoint(cx.w, o, id, a), CONTACT_RANGE) => {
            let (give, receive, weapon_trade) =
                if action == 7 && target < BARGAIN_STAT_COUNT && has_spare_weapon(cx.w, player) {
                    (None, target, true)
                } else if action == 5
                    && target < BARGAIN_STAT_COUNT * BARGAIN_STAT_COUNT
                    && target / BARGAIN_STAT_COUNT != target % BARGAIN_STAT_COUNT
                    && bargain_stacks(player, target / BARGAIN_STAT_COUNT) >= BARGAIN_COST
                {
                    (
                        Some(target / BARGAIN_STAT_COUNT),
                        target % BARGAIN_STAT_COUNT,
                        false,
                    )
                } else {
                    return;
                };
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.reward = PendingReward {
                give,
                receive: Some(receive),
                weapon_trade,
                points: 0,
            };
            a.achieve(1);
        }
        (6, StayAWhile) if near(cx.k, cx.w, pos, o.at, CONTACT_RANGE) => {
            let a = board_mut(cx.w).unwrap().get_mut(owner, offer_id).unwrap();
            a.meditation = Some(pos);
            a.dwell_ms = 0.;
        }
        _ => {}
    }
}

fn inside_fort(k: &MatchStatic, at: Vec2, pos: Vec2) -> bool {
    let Some(layers) = k.arena.map.layers() else {
        return distance(at, pos) <= FORT_RANGE;
    };
    let tile = crate::map::world_to_tile;
    let contains = |b: &crate::mapdata::TileRect, p: Vec2| {
        let (x, y) = (tile(p.x), tile(p.y));
        let (left, top) = (i64::from(b.x), i64::from(b.y));
        x >= left && y >= top && x < left + i64::from(b.w) && y < top + i64::from(b.h)
    };
    layers
        .data()
        .features
        .iter()
        .find(|feature| feature.variant == "guardHouse" && contains(&feature.bbox, at))
        .is_some_and(|feature| contains(&feature.bbox, pos))
}

pub(super) fn tick(cx: &mut Cx, i: usize, id: QuestId) {
    let b = board(cx.w).unwrap();
    let a = &b.attempts[i];
    let o = offer(cx.w, a.offer).unwrap();
    let owner = a.owner;
    let p = cx.w.player(owner).unwrap();
    let pos = p.pos();
    let destination = endpoint(cx.w, o, id, a);
    let at_end = near(cx.k, cx.w, pos, destination, CONTACT_RANGE);
    // A scared giver hides only itself: a turn-in at a remote contact, trader or rival outpost still lands.
    let at_giver = ends_at_giver(cx.w, o, id, a);
    let mut next = a.clone();
    if !next.ready {
        match id {
            SmugglersRun | TwoBanners if next.carrying && at_end => next.achieve(1),
            BarrowRun | Watchtowers | PilgrimsPath => {
                for (j, &at) in o.tokens.iter().enumerate() {
                    if near(cx.k, cx.w, pos, at, CONTACT_RANGE) {
                        next.claim_anchor(j as u8);
                    }
                }
                if next.progress as usize == o.tokens.len() && !o.tokens.is_empty() {
                    next.achieve(next.progress);
                }
            }
            HoldTheFort => {
                let inside = o
                    .tokens
                    .first()
                    .is_some_and(|&at| inside_fort(cx.k, at, pos));
                let contested = o.tokens.first().is_some_and(|&at| {
                    cx.w.players.iter().any(|other| {
                        other.id != owner && other.standing() && inside_fort(cx.k, at, other.pos())
                    })
                });
                if inside && !contested {
                    next.dwell_ms = (next.dwell_ms + cx.k.tick_ms()).min(FORT_MS);
                    if next.dwell_ms >= FORT_MS {
                        next.achieve(1);
                    }
                } else if !inside {
                    next.dwell_ms = 0.;
                }
            }
            StayAWhile if next.meditation.is_some() => {
                if !at_end
                    || next.meditation != Some(pos)
                    || crate::status_runtime::action_blocked(&p.statuses, cx.w.server_time)
                    || (p.weapon.action.is_some() || p.weapon.held)
                {
                    next.meditation = None;
                    next.dwell_ms = 0.;
                } else {
                    next.dwell_ms = (next.dwell_ms + cx.k.tick_ms()).min(MEDITATE_MS);
                    if next.dwell_ms >= MEDITATE_MS {
                        next.achieve(1);
                    }
                }
            }
            _ => {}
        }
    }
    if at_end && (!at_giver || visible_giver(cx.w, owner, next.offer)) {
        if id == TheBurningCrown {
            if let Some(power) = crown_power(cx.w, owner) {
                next.reward.points = power;
                next.achieve(1);
            }
        }
        if next.ready {
            next.pending = Some(next.reward);
            next.deadline = 0.;
            next.meditation = None;
        }
    }
    board_mut(cx.w).unwrap().attempts[i] = next;
}

pub(crate) fn objective_for_owner(
    w: &World,
    owner: PlayerId,
    offer_id: u32,
    index: u8,
) -> Option<Vec2> {
    let a = attempt(w, owner, offer_id)?;
    if a.completed || a.pending.is_some() {
        return None;
    }
    let o = offer(w, offer_id)?;
    let id = kind(o)?;
    if a.ready {
        return (index == 0).then(|| endpoint(w, o, id, a));
    }
    let b = board(w)?;
    let player = w.player(owner)?;
    let at = || o.tokens.get(index as usize).copied();
    match id {
        TheBurningCrown if index == 0 && b.crown.carrier == Some(owner) => Some(o.at),
        TheBurningCrown if index == 0 => crown_ground(b, o),
        TheWarden if index == 1 => at(),
        TheWarden | ABoneToPick | EliteSlayer | RimeHunt => b
            .targets
            .iter()
            .filter(|t| t.offer == offer_id && t.alive)
            .nth(index as usize)
            .and_then(|t| w.mobs.get(t.actor))
            .map(|m| m.core.pos()),
        BountyHunter if index == 0 => b
            .bounty
            .and_then(|target| w.player(target))
            .map(|p| p.pos()),
        ACruelJoke if index < 3 && a.bits & (1 << index) == 0 => {
            prank_contact(w, offer_id, index).map(|(_, p)| p)
        }
        TradeUp if index == a.progress => trader_at(w, o, index),
        FeedOrHunt if index == 0 => b
            .targets
            .iter()
            .find(|t| t.offer == offer_id && t.alive)
            .and_then(|t| w.mobs.get(t.actor))
            .map(|m| m.core.pos()),
        TwoBanners if index == 0 && a.branch == Some(1) && a.carrying => Some(o.at),
        TwoBanners if a.branch.is_some() && index == 0 => at(),
        SmugglersRun if a.carrying && index == 0 => Some(o.at),
        WhispersInTheBrush if index < 3 => at().filter(|&p| {
            a.bits & (1 << index) == 0 && distance(player.pos(), p) <= BRUSH_REVEAL_RANGE
        }),
        StayAWhile if index == 0 => Some(o.at),
        TheBurningCrown | BountyHunter | ACruelJoke | TradeUp | TwoBanners => None,
        _ if index < 8 && a.bits & (1 << index) == 0 => at(),
        _ => None,
    }
}

pub(super) fn trader_at(w: &World, o: &Offer, index: u8) -> Option<Vec2> {
    let i = index as usize;
    board(w)?
        .traders
        .get(i)
        .copied()
        .flatten()
        .or_else(|| o.tokens.get(i).copied())
}

pub(super) fn walk_traders(k: &MatchStatic, w: &mut World) {
    let Some(o) = w
        .quests
        .as_ref()
        .and_then(|q| q.offers.iter().find(|o| kind(o) == Some(TradeUp)))
    else {
        return;
    };
    let Some(&[a, b, c]) = o.tokens.get(..3) else {
        return;
    };
    let map = crate::collision::open(k, w);
    let now = w.server_time;
    let positions = [a, b, c].map(Some);
    let mut walked = positions;
    for (i, from) in [a, b, c].into_iter().enumerate() {
        // Each contact patrols a short, body-clear stretch near the chosen
        // waypoint. Fixed phase offsets keep the route reproducible after restore.
        let phase = ((now + 3000. * i as f64) % 12_000.) / 6000.;
        let fraction = 1. - (phase - 1.).abs();
        let vec = [
            Vec2::new(72., 0.),
            Vec2::new(0., 72.),
            Vec2::new(-72., 0.),
            Vec2::new(0., -72.),
        ];
        if let Some(offset) = vec.into_iter().find(|d| {
            crate::geometry::segment_clear(
                map,
                from,
                Vec2::new(from.x + d.x, from.y + d.y),
                crate::constants::PLAYER_RADIUS,
                crate::map::MASK_MOVE,
            )
        }) {
            walked[i] = Some(Vec2::new(
                from.x + offset.x * fraction,
                from.y + offset.y * fraction,
            ));
        }
    }
    board_mut(w).unwrap().traders = walked;
}

pub(super) fn has_spare_weapon(w: &World, player: &crate::state::Player) -> bool {
    if crate::quests::board::rewards::effects::locked_gear(w, player.id) {
        return false;
    }
    if player.weapon.slot == 0 {
        player.weapon.weapon1.is_some()
    } else {
        player.weapon.weapon0.is_some()
    }
}
