//! Personal quest objectives and shared physical quest resources.
mod choices;
pub(crate) use choices::{at_turn_in, choices};
mod interactions;
#[cfg(test)]
mod scenarios;
mod signals;
pub(crate) mod targets;
#[cfg(test)]
mod tests;

pub(crate) use interactions::{interact, objective_for_owner, visible_giver};
pub(crate) use signals::{
    bargain_stacks, bargain_stat, bargain_take, bind_target, on_bounty_kill, on_camp_cleared,
    on_drop_body, on_hit_landed, on_mob_killed, on_player_killed, on_target_killed, on_teleported,
    BARGAIN_COST, BARGAIN_STAT_COUNT,
};

use self::signals::BARGAIN_STAT_NAMES;
use super::{catalog::QuestId, quest};
use crate::constants::{PLAYER_SPEED, QUEST_TURN_IN_RANGE};
use crate::hooks::Hx;
use crate::state::{Phase, PlayerId, World};
use crate::statics::MatchStatic;
use crate::systems::Cx;
use crate::Vec2;
use serde::{Deserialize, Serialize};

pub(crate) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for value in [FORT_MS, MEDITATE_MS, CROWN_BURN_RATE] {
        word(value.to_bits());
    }
    word(u64::from(CROWN_POWER_CAP));
    interactions::visit_rules(word);
    targets::visit_rules(word);
    word(signals::BARGAIN_COST);
    word(u64::from(signals::BARGAIN_STAT_COUNT));
    for name in signals::BARGAIN_STAT_NAMES {
        word(name.len() as u64);
        for byte in name.bytes() {
            word(u64::from(byte));
        }
    }
}

const FORT_MS: f64 = 45_000.;
const MEDITATE_MS: f64 = 30_000.;
const CROWN_BURN_RATE: f64 = 0.01;
const CROWN_POWER_CAP: u8 = 48;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub(crate) struct PendingReward {
    pub(crate) points: u8,
    pub(crate) give: Option<u8>,
    pub(crate) receive: Option<u8>,
    pub(crate) weapon_trade: bool,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Attempt {
    owner: PlayerId,
    offer: u32,
    deadline: f64,
    progress: u8,
    bits: u8,
    branch: Option<u8>,
    carrying: bool,
    ready: bool,
    completed: bool,
    pending: Option<PendingReward>,
    reward: PendingReward,
    dwell_ms: f64,
    meditation: Option<Vec2>,
    damaged: [Option<PlayerId>; 2],
    lock: u32,
}

impl Attempt {
    fn new(deadline: f64, lock: u32) -> Self {
        Self {
            owner: PlayerId(0),
            offer: 0,
            deadline,
            progress: 0,
            bits: 0,
            branch: None,
            carrying: false,
            ready: false,
            completed: false,
            pending: None,
            reward: PendingReward::default(),
            dwell_ms: 0.,
            meditation: None,
            damaged: [None; 2],
            lock,
        }
    }

    fn claim_anchor(&mut self, index: u8) -> bool {
        let bit = 1u8 << index;
        if self.bits & bit != 0 {
            return false;
        }
        self.bits |= bit;
        self.progress += 1;
        true
    }

    fn achieve(&mut self, count: u8) {
        self.progress = count;
        self.ready = true;
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Target {
    offer: u32,
    actor: PlayerId,
    alive: bool,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Crown {
    carrier: Option<PlayerId>,
    ground: Option<Vec2>,
    elapsed_ms: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct FledGiver {
    owner: PlayerId,
    giver_offer: u32,
    until: f64,
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct BoardState {
    attempts: Vec<Attempt>,
    crown: Crown,
    targets: Vec<Target>,
    seen_actor: u32,
    fled_givers: Vec<FledGiver>,
    traders: [Option<Vec2>; 3],
    bounty: Option<PlayerId>,
    lock: u32,
    locked: bool,
}

impl BoardState {
    fn get(&self, owner: PlayerId, offer: u32) -> Option<&Attempt> {
        self.attempts
            .iter()
            .find(|a| a.owner == owner && a.offer == offer)
    }
    fn get_mut(&mut self, owner: PlayerId, offer: u32) -> Option<&mut Attempt> {
        self.attempts
            .iter_mut()
            .find(|a| a.owner == owner && a.offer == offer)
    }
    fn start(&mut self, owner: PlayerId, offer: u32, deadline: f64, lock: u32) -> bool {
        if self.get(owner, offer).is_some() {
            return false;
        }
        self.attempts.push(Attempt {
            owner,
            offer,
            ..Attempt::new(deadline, lock)
        });
        true
    }
    fn abandon(&mut self, owner: PlayerId, offer: u32) -> bool {
        let Some(i) = self.attempts.iter().position(|a| {
            a.owner == owner && a.offer == offer && !a.completed && a.pending.is_none()
        }) else {
            return false;
        };
        self.attempts.remove(i);
        true
    }
    fn finish(&mut self, owner: PlayerId, offer: u32) -> bool {
        let Some(a) = self.get_mut(owner, offer) else {
            return false;
        };
        if a.completed || a.pending.take().is_none() {
            return false;
        }
        a.completed = true;
        a.carrying = false;
        a.deadline = 0.;
        true
    }
}

type Offer = super::super::live::Offer;

fn board(w: &World) -> Option<&BoardState> {
    w.quests.as_ref()?.board.as_ref()
}
fn board_mut(w: &mut World) -> Option<&mut BoardState> {
    w.quests.as_mut()?.board.as_mut()
}
fn offer(w: &World, id: u32) -> Option<&Offer> {
    w.quests.as_ref()?.offers.iter().find(|o| o.id == id)
}
fn kind(o: &Offer) -> Option<QuestId> {
    quest(usize::from(o.def))
}
fn attempt(w: &World, owner: PlayerId, offer: u32) -> Option<&Attempt> {
    board(w)?.get(owner, offer)
}
fn owner_allowed(w: &World, owner: PlayerId) -> bool {
    w.phase == Phase::Active && w.player(owner).is_some_and(|p| !p.seat.bot && p.standing())
}

pub(crate) fn is_accepted(w: &World, owner: PlayerId, offer: u32) -> bool {
    attempt(w, owner, offer).is_some_and(|a| !a.completed)
}
pub(crate) fn is_completed(w: &World, owner: PlayerId, offer: u32) -> bool {
    attempt(w, owner, offer).is_some_and(|a| a.completed)
}
pub(crate) fn progress(w: &World, owner: PlayerId, offer_id: u32) -> u8 {
    let Some(a) = attempt(w, owner, offer_id) else {
        return 0;
    };
    match offer(w, offer_id).and_then(kind) {
        // Dwell descriptions specify their required seconds. Project elapsed
        // whole seconds while retaining the single objective completion flag.
        Some(QuestId::HoldTheFort | QuestId::StayAWhile) => (a.dwell_ms / 1000.) as u8,
        _ => a.progress,
    }
}
pub(crate) fn deadline(w: &World, owner: PlayerId, offer: u32) -> f64 {
    attempt(w, owner, offer).map_or(0., |a| a.deadline)
}
pub(crate) fn ready(w: &World, owner: PlayerId, offer: u32) -> bool {
    attempt(w, owner, offer).is_some_and(|a| a.ready && !a.completed)
}
/// The provisional power backed by the one physical crown.
pub(crate) fn crown_power(w: &World, owner: PlayerId) -> Option<u8> {
    let crown = board(w)?.crown;
    (crown.carrier == Some(owner)).then(|| ((crown.elapsed_ms / 1000.) as u8).min(CROWN_POWER_CAP))
}

/// A deferred choice can survive death even though a dead seat cannot turn it in.
pub(crate) fn has_pending_choice(w: &World, owner: PlayerId, offer: u32) -> bool {
    attempt(w, owner, offer).is_some_and(|a| a.pending.is_some())
}

pub(crate) fn pending(w: &World, owner: PlayerId, offer: u32) -> Option<PendingReward> {
    if !owner_allowed(w, owner) {
        return None;
    }
    attempt(w, owner, offer)?.pending
}
pub(crate) fn finish(w: &mut World, owner: PlayerId, offer: u32) -> bool {
    if pending(w, owner, offer).is_none() {
        return false;
    }
    let crown = self::offer(w, offer).and_then(kind) == Some(QuestId::TheBurningCrown);
    let state = board_mut(w).unwrap();
    if !state.finish(owner, offer) {
        return false;
    }
    if crown && state.crown.carrier == Some(owner) {
        state.crown = Crown::default();
    }
    true
}

/// Return to the hag's choices if the selected payment is no longer
/// available by the time the authoritative reward transaction runs.
pub(crate) fn reopen_bargain(w: &mut World, owner: PlayerId, offer_id: u32) {
    if offer(w, offer_id).and_then(kind) != Some(QuestId::TheBargain) {
        return;
    }
    if let Some(a) = board_mut(w).and_then(|state| state.get_mut(owner, offer_id)) {
        if !a.completed && a.pending.take().is_some() {
            a.ready = false;
            a.progress = 0;
            a.reward = PendingReward::default();
        }
    }
}

pub(crate) fn accept(cx: &mut Cx, oi: usize, owner: PlayerId) {
    if cx.w.rt.gates_locked || !owner_allowed(cx.w, owner) {
        return;
    }
    let Some(q) = cx.w.quests.as_ref() else {
        return;
    };
    let Some(o) = q.offers.get(oi) else { return };
    let Some(id) = kind(o) else { return };
    if !visible_giver(cx.w, owner, o.id) {
        return;
    }
    let deadline = id.definition().route_timer_ms.map_or(0., |base| {
        let length = route_length(cx.w, crate::collision::open(cx.k, cx.w), cx.pf, o, id);
        cx.w.server_time + (base as f64).max(length * 1500. / PLAYER_SPEED + 15_000.)
    });
    let offer = o.id;
    let Some(state) = board_mut(cx.w) else { return };
    if state.start(owner, offer, deadline, state.lock) {
        targets::refresh(cx.k, cx.w);
        super::super::notices::owner(cx.ev, owner, format!("Accepted: {}", id.definition().name));
    }
}

fn distance(a: Vec2, b: Vec2) -> f64 {
    crate::math::hypot(a.x - b.x, a.y - b.y)
}

fn near(k: &MatchStatic, w: &World, pos: Vec2, at: Vec2, range: f64) -> bool {
    distance(pos, at) <= range
        && crate::geometry::has_line_of_sight(
            crate::collision::map(k, w),
            pos,
            at,
            crate::map::MASK_SIGHT,
        )
}

fn route_length(
    w: &World,
    map: &crate::TileMap,
    pathfinder: &mut crate::pathfind::Pathfinder,
    o: &Offer,
    id: QuestId,
) -> f64 {
    let mut last = o.at;
    let mut length = 0.;
    let mut leg = |at| {
        length += path_length(pathfinder, map, last, at);
        last = at;
    };
    match id {
        QuestId::ACruelJoke => {
            for index in 0..3 {
                if let Some((_, at)) = interactions::prank_contact(w, o.id, index) {
                    leg(at);
                }
            }
        }
        QuestId::FeedOrHunt => {
            for &at in o.tokens.iter().skip(1).chain(o.tokens.first()) {
                leg(at);
            }
        }
        _ => {
            for &at in &o.tokens {
                leg(at);
            }
        }
    }
    if !matches!(
        id,
        QuestId::Letters | QuestId::TuberEats | QuestId::EvenSkeletonsGetCold | QuestId::TradeUp
    ) {
        leg(o.at);
    }
    length
}

fn path_length(
    pathfinder: &mut crate::pathfind::Pathfinder,
    map: &crate::TileMap,
    from: Vec2,
    to: Vec2,
) -> f64 {
    let path = pathfinder.find_path(map, from, to, crate::constants::PLAYER_RADIUS);
    core::iter::once(from)
        .chain(path.iter().copied())
        .zip(path.iter().copied())
        .map(|(a, b)| distance(a, b))
        .sum::<f64>()
        .max(distance(from, to))
}

fn endpoint(w: &World, o: &Offer, id: QuestId, a: &Attempt) -> Vec2 {
    use QuestId::*;
    match id {
        Letters | TuberEats | EvenSkeletonsGetCold => o.tokens.first().copied().unwrap_or(o.at),
        TwoBanners if a.branch == Some(0) => o.tokens.first().copied().unwrap_or(o.at),
        TradeUp => a
            .progress
            .checked_sub(1)
            .and_then(|stage| interactions::trader_at(w, o, stage))
            .unwrap_or(o.at),
        TheBargain => o.tokens.first().copied().unwrap_or(o.at),
        _ => o.at,
    }
}

/// Does this attempt turn in at its own giver? Remote contacts, the hag, the rival outpost and a Trade Up trader are
/// separate people, so a giver scared off by A Cruel Joke does not hide them.
fn ends_at_giver(w: &World, o: &Offer, id: QuestId, a: &Attempt) -> bool {
    use QuestId::*;
    match id {
        Letters | TuberEats | EvenSkeletonsGetCold | TheBargain => false,
        TwoBanners => a.branch != Some(0),
        TradeUp => a
            .progress
            .checked_sub(1)
            .and_then(|stage| interactions::trader_at(w, o, stage))
            .is_none(),
        _ => true,
    }
}

pub(crate) fn public_markers(
    w: &World,
    offer_id: u32,
) -> impl Iterator<Item = (PlayerId, Vec2)> + '_ {
    let state = board(w);
    let id = offer(w, offer_id).and_then(kind);
    let dedicated = match (state, id) {
        (Some(state), Some(QuestId::TheBurningCrown)) => state.crown.carrier,
        (Some(state), Some(QuestId::BountyHunter)) => state.bounty,
        _ => None,
    };
    dedicated
        .into_iter()
        .chain(
            state
                .into_iter()
                .flat_map(|b| b.attempts.iter())
                .filter_map(move |a| {
                    let mark = match id {
                        Some(QuestId::SmugglersRun) => a.carrying,
                        Some(QuestId::HoldTheFort) => a.dwell_ms > 0. && !a.ready,
                        Some(QuestId::StayAWhile) => a.meditation.is_some() && !a.ready,
                        _ => false,
                    };
                    (a.offer == offer_id && !a.completed && mark).then_some(a.owner)
                }),
        )
        .filter_map(|carrier| {
            let p = w.player(carrier)?;
            p.standing().then_some((carrier, p.pos()))
        })
}

pub(crate) fn system(cx: &mut Cx) {
    if cx.w.phase != Phase::Active || board(cx.w).is_none() {
        return;
    }
    let now = cx.w.server_time;
    board_mut(cx.w)
        .unwrap()
        .fled_givers
        .retain(|g| g.until > now);
    targets::refresh(cx.k, cx.w);
    interactions::walk_traders(cx.k, cx.w);
    signals::update_lock(cx.w);
    signals::update_bounty(cx.w);
    signals::burn_crown(cx);
    let mut i = 0;
    while let Some(a) = board(cx.w).and_then(|b| b.attempts.get(i)) {
        let (owner, offer_id) = (a.owner, a.offer);
        if a.completed || a.pending.is_some() {
            i += 1;
            continue;
        }
        let locked = cx.w.rt.gates_locked;
        if !owner_allowed(cx.w, owner)
            || (!locked && a.deadline > 0. && a.deadline <= cx.w.server_time)
        {
            interactions::abandon(cx.w, owner, offer_id);
            continue;
        }
        let Some(o) = offer(cx.w, offer_id) else {
            i += 1;
            continue;
        };
        let Some(id) = kind(o) else {
            i += 1;
            continue;
        };
        if locked {
            let a = &mut board_mut(cx.w).unwrap().attempts[i];
            if a.deadline > 0. {
                a.deadline += cx.k.tick_ms();
            }
        } else {
            interactions::tick(cx, i, id);
        }
        i += 1;
    }
}

/// A player leaves the match (`lifecycle::remove` or `abandon`): their open and pending attempts end, a carried crown
/// falls where they stood, and a bounty mark on them clears. Completed records stay, named through the roster.
pub(crate) fn departed(w: &mut World, id: PlayerId) {
    let at = w.player(id).map(|p| p.pos());
    let Some(state) = board_mut(w) else { return };
    if state.crown.carrier == Some(id) {
        state.crown.carrier = None;
        state.crown.ground = at;
    }
    if state.bounty == Some(id) {
        state.bounty = None;
    }
    state.attempts.retain(|a| a.owner != id || a.completed);
    state.fled_givers.retain(|g| g.owner != id);
}

fn valid_reward_payload(id: QuestId, a: &Attempt) -> bool {
    if a.pending.is_some_and(|pending| pending != a.reward) {
        return false;
    }
    if !a.ready {
        return a.reward == PendingReward::default();
    }
    let PendingReward {
        points,
        give,
        receive,
        weapon_trade,
    } = a.reward;
    match id {
        QuestId::TheBurningCrown => {
            points <= CROWN_POWER_CAP && give.is_none() && receive.is_none() && !weapon_trade
        }
        QuestId::TradeUp => {
            (1..=3).contains(&points)
                && points == a.progress
                && give.is_none()
                && receive.is_none()
                && !weapon_trade
        }
        QuestId::TheBargain => {
            points == 0
                && receive.is_some_and(|index| index < BARGAIN_STAT_COUNT)
                && if weapon_trade {
                    give.is_none()
                } else {
                    give.is_some_and(|index| index < BARGAIN_STAT_COUNT && Some(index) != receive)
                }
        }
        _ => a.reward == PendingReward::default(),
    }
}

pub(crate) fn validate(k: &MatchStatic, w: &World) -> Result<(), String> {
    let Some(state) = board(w) else { return Ok(()) };
    if state.seen_actor > w.next_id
        || state
            .bounty
            .is_some_and(|id| w.player(id).is_none_or(|p| !p.seat.bot))
        || state
            .traders
            .iter()
            .flatten()
            .any(|at| !at.x.is_finite() || !at.y.is_finite())
    {
        return Err("invalid shared quest entities".into());
    }
    for (i, a) in state.attempts.iter().enumerate() {
        let Some(id) = offer(w, a.offer).and_then(kind) else {
            return Err("quest attempt names an unknown board offer".into());
        };
        // A leaver keeps their completed record (the roster keeps their name); only a live seat can be a bot.
        if w.roster.name(a.owner).is_none()
            || w.player(a.owner).is_some_and(|p| p.seat.bot)
            || state.attempts[..i]
                .iter()
                .any(|prev| prev.owner == a.owner && prev.offer == a.offer)
            || !a.deadline.is_finite()
            || a.deadline < 0.
            || !a.dwell_ms.is_finite()
            || a.dwell_ms < 0.
            || a.dwell_ms > FORT_MS
            || a.progress
                > id.definition()
                    .objective_count
                    .max(u8::from(id == QuestId::BarrowRun) * 3)
            || a.branch
                .is_some_and(|branch| id != QuestId::TwoBanners || branch > 1)
            || a.meditation.is_some_and(|at| {
                id != QuestId::StayAWhile || !at.x.is_finite() || !at.y.is_finite()
            })
            || a.damaged.iter().flatten().any(|&target| {
                id != QuestId::Gatecrasher || target == a.owner || w.roster.name(target).is_none()
            })
            || (a.damaged[0] == a.damaged[1] && a.damaged[0].is_some())
            || !valid_reward_payload(id, a)
            || (a.pending.is_some() && (!a.ready || a.completed))
            || (a.completed && !a.ready)
        {
            return Err("invalid personal quest attempt".into());
        }
    }
    if let Some(carrier) = state.crown.carrier {
        if !w
            .player(carrier)
            .is_some_and(|p| !p.seat.bot && p.standing())
            || state.crown.ground.is_some()
            || !state.attempts.iter().any(|a| {
                a.owner == carrier
                    && a.carrying
                    && !a.completed
                    && offer(w, a.offer).and_then(kind) == Some(QuestId::TheBurningCrown)
            })
        {
            return Err("invalid crown carrier".into());
        }
    }
    if !state.crown.elapsed_ms.is_finite()
        || state.crown.elapsed_ms < 0.
        || state
            .crown
            .ground
            .is_some_and(|at| !at.x.is_finite() || !at.y.is_finite())
    {
        return Err("invalid crown state".into());
    }
    for (i, target) in state.targets.iter().enumerate() {
        if offer(w, target.offer)
            .and_then(kind)
            .is_none_or(|quest| !targets::matches_actor(k, w, target.actor, quest))
            || (target.alive
                && w.mobs
                    .get(target.actor)
                    .is_none_or(|m| m.status != crate::state::Status::Alive))
            || (!target.alive
                && w.mobs
                    .get(target.actor)
                    .is_some_and(|m| m.status == crate::state::Status::Alive))
            || state.targets[..i].iter().any(|p| p.actor == target.actor)
        {
            return Err("invalid quest target".into());
        }
    }
    for (i, giver) in state.fled_givers.iter().enumerate() {
        if w.roster.name(giver.owner).is_none()
            || offer(w, giver.giver_offer).and_then(kind).is_none()
            || !giver.until.is_finite()
            || giver.until <= 0.
            || state.fled_givers[..i]
                .iter()
                .any(|g| g.owner == giver.owner && g.giver_offer == giver.giver_offer)
        {
            return Err("invalid fled quest giver".into());
        }
    }
    Ok(())
}
