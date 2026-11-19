//! Full producers and isolated trusted status facts have disjoint admission domains.
use super::*;
// Retained source clocks cannot prove the full history. These are necessary stock
// equations: opening lock, or a prior accepted fixed-step cast plus its cooldown.
fn prior_clock(prior: f64, initial: f64, first_cast: f64, accepted: f64, cooldown: f64) -> bool {
    prior <= accepted
        && (prior == initial
            || (prior >= first_cast + cooldown
                && (prior - cooldown) % crate::constants::TICK_MS == 0.))
}
/// `prior_clock` for a clock an earlier cast may have produced under any of the Wish cooldowns `cooldowns` up to
/// `upto` (the accepted cast's own): a grant between the casts can have lengthened it (`wish_cooldowns`).
fn prior_clock_any(
    prior: f64,
    initial: f64,
    first_cast: f64,
    accepted: f64,
    cooldowns: [f64; 2],
    upto: f64,
) -> bool {
    cooldowns
        .into_iter()
        .any(|cd| cd <= upto && prior_clock(prior, initial, first_cast, accepted, cd))
}
/// The lattice charge clocks stay on: the tick and every Wish cooldown a spend can have had (`wish_cooldowns`).
fn charge_clock_lattice(cooldowns: [f64; 2]) -> f64 {
    let mut a = crate::constants::TICK_MS;
    for cooldown in cooldowns {
        let mut b = cooldown;
        while b != 0. {
            (a, b) = (b, a % b);
        }
    }
    a
}

/// The Wish cooldowns a spend of this player's Wish can carry: the faction Wish's cooldown with no multiplier (a cast
/// before the build held a `WishCooldown` card) and with the build's multiplier now. A build only grows, and exactly one
/// card, unique, carries a `WishCooldown` row, a factor of at least 1 (`wish_cooldown_lever_tests`), so every cast's
/// cooldown is one of the two and a later cast's is never shorter than an earlier one's. The stored spend keeps the
/// cooldown it was cast with: a grant between two casts (a chest pick of Arcane Overload while the Wish cools down)
/// changes the multiplier now, not the spend.
fn wish_cooldowns(
    k: &crate::statics::MatchStatic,
    ability: AbilityId,
    wish_mult: f64,
) -> [f64; 2] {
    [
        cooldown(ability, k.config.training, None),
        cooldown(ability, k.config.training, Some(wish_mult)),
    ]
}

/// The card producer's trace: Wishful Thinking records each cut (source cards.trims) in the source window. The
/// clocks a cut leaves cannot be replayed from the last spend alone, so the necessary equations use this bounded trace.
/// `budget` is the world table's window budget for the current spend (`CardNumbers::wishful_budget`): under a share
/// rule it is a share of the spend's full cooldown, the longest any trim in the window was cut under (a cooldown only
/// lengthens between casts, `wish_cooldowns`), so it bounds the earlier casts' trims too.
fn trim_trace_coherent(
    p: &crate::state::Player,
    numbers: &crate::cards::CardNumbers,
    budget: f64,
    now: f64,
    raid_started_at: f64,
) -> bool {
    let window = numbers.wishful_window_ms;
    let mut in_window = 0.;
    for t in &p.card_trims {
        if !t.at.is_finite()
            || !t.ms.is_finite()
            || t.at < raid_started_at
            || t.at > now
            || t.at % crate::constants::TICK_MS != 0.
            || !(t.ms > 0. && t.ms <= budget)
        {
            return false;
        }
        if now - t.at < window {
            in_window += t.ms;
        }
    }
    in_window <= budget
}

/// The source caps a window's cuts at the budget, so no clock can be further than one budget per elapsed window under
/// its untrimmed spend value (a necessary bound, not an exact replay).
fn max_trim_cut(numbers: &crate::cards::CardNumbers, budget: f64, elapsed: f64) -> f64 {
    budget * (1. + elapsed.max(0.) / numbers.wishful_window_ms)
}

pub(crate) fn validate(
    k: &crate::statics::MatchStatic,
    w: &crate::state::World,
) -> Result<(), String> {
    if !k.config.wish_lock_ms.is_finite()
        || k.config.wish_lock_ms < 0.
        || k.config.wish_lock_ms > f64::from(u32::MAX) * crate::constants::TICK_MS
    {
        return Err("invalid Wish lock".into());
    }
    let initial = if k.config.training {
        0.
    } else {
        w.raid_started_at + k.config.wish_lock_ms
    };
    let first_cast = (w.raid_started_at + crate::constants::TICK_MS).max(initial);
    let full = k.config.abilities == Mode::Full;
    if full {
        super::effect_checkpoint::validate(w)?;
    }
    for p in &w.players {
        crate::cards::fight_clock::validate(k, w, p)?;
        let stocks = p.stocks;
        let cooldowns = wish_cooldowns(k, wish(p.faction()), p.modifiers.wish_cooldown_mult());
        if let Some(spend) = p.card_runtime.and_then(|cards| cards.wish_cast) {
            if !full {
                return Err("card Wish memory in Off mode".into());
            }
            if !(first_cast..=w.server_time).contains(&spend.at)
                || spend.at % crate::constants::TICK_MS != 0.
                || !spend.cooldown_ms.is_finite()
                || spend.cooldown_ms <= 0.
                || spend.cooldown_ms.fract() != 0.
                || !cooldowns.contains(&spend.cooldown_ms)
            {
                return Err("invalid card Wish spend".into());
            }
        }
        if !full && stocks != Stocks::default() {
            return Err("Wish stocks in Off mode".into());
        }
        if ![
            Some(stocks.wish_ready_at),
            Some(stocks.q_ready_at),
            Some(stocks.e_ready_at),
            stocks.wish_full_at,
        ]
        .into_iter()
        .flatten()
        .all(|v| v.is_finite() && v >= 0. && v <= f64::from(u32::MAX) * crate::constants::TICK_MS)
        {
            return Err("invalid Wish stock clock".into());
        }
        if full {
            let begun = w.raid_started_at > 0.;
            if !begun {
                if stocks != Stocks::default()
                    || p.card_runtime.and_then(|cards| cards.wish_cast).is_some()
                {
                    return Err("Wish state before raid start".into());
                }
            } else {
                let Some(full_at) = stocks.wish_full_at else {
                    return Err("missing initialized Wish stock".into());
                };
                let has_spend = p.card_runtime.and_then(|c| c.wish_cast).is_some();
                if !has_spend && (stocks.wish_ready_at != initial || full_at != initial) {
                    return Err("unproduced Wish stock clocks".into());
                }
                let def = definition(wish(p.faction()));
                // The spend's own cooldown (checked above to be one a cast can have had); the multiplier now may be
                // a later grant's. Without a spend the clocks are the opening lock and no equation reads it.
                let recharge = p
                    .card_runtime
                    .and_then(|c| c.wish_cast)
                    .map_or(cooldowns[1], |spend| spend.cooldown_ms);
                // The shortest cooldown the cast before this spend can have had.
                let prior_min = cooldowns[0].min(cooldowns[1]);
                let lattice = charge_clock_lattice(cooldowns);
                let numbers = k.cards.numbers();
                let budget = numbers.wishful_budget(recharge);
                // A card-owned Wishful Thinking trim is a second producer of the same clocks: the source stores only
                // its window trace, so the untrimmed equations are replaced by that trace plus bounded necessary ones.
                let trimmed = has_spend
                    && !p.card_trims.is_empty()
                    && p.build.owns(crate::cards::CardId::WishfulThinking);
                if trimmed {
                    if !trim_trace_coherent(p, numbers, budget, w.server_time, w.raid_started_at) {
                        return Err("incoherent card Wish trim trace".into());
                    }
                    if stocks.wish_ready_at > full_at
                        || (def.charges == 1 && stocks.wish_ready_at != full_at)
                    {
                        return Err("incoherent Wish charge clocks".into());
                    }
                } else if stocks.wish_ready_at > full_at
                    || (stocks.wish_ready_at != full_at
                        && (def.charges != 2
                            || stocks.wish_ready_at != (full_at - recharge).max(0.)))
                {
                    return Err("incoherent Wish charge clocks".into());
                }
                if has_spend && (full_at < initial || stocks.wish_ready_at < initial) {
                    return Err("Wish clocks precede opening lock".into());
                }
                if let Some(spend) = p.card_runtime.and_then(|c| c.wish_cast) {
                    let spent = if def.charges == 1 {
                        full_at == spend.at + recharge && stocks.wish_ready_at == full_at
                    } else {
                        // A charge was available at acceptance: the previous full clock lay
                        // at most one recharge ahead. The new interval follows spendCharge.
                        // The previous interval was the previous cast's cooldown, at least `prior_min`; the last
                        // bound replays one earlier cast and holds only when every cast had this spend's cooldown.
                        (spend.at + recharge..=spend.at + 2. * recharge).contains(&full_at)
                            && stocks.wish_ready_at == (full_at - recharge).max(0.)
                            && full_at % lattice == 0.
                            && (full_at == spend.at + recharge
                                || (spend.at >= first_cast + crate::constants::TICK_MS
                                    && full_at >= first_cast + prior_min + recharge
                                    && (prior_min < recharge
                                        || spend.at >= first_cast + recharge
                                        || full_at
                                            <= spend.at - crate::constants::TICK_MS
                                                + 2. * recharge)))
                    };
                    // The card producer: a cut only lowers the untrimmed spend clock, never above it, and the
                    // per-window budget bounds how far (a necessary equation, not an exact replay of the cuts).
                    // Millisecond cuts (the legacy rule) keep the clocks on the tick lattice; a share rule's cuts are
                    // shares of the cooldown, off it.
                    let max_cut = max_trim_cut(numbers, budget, w.server_time - spend.at);
                    let on_lattice = numbers.wishful_hit_share > 0.
                        || numbers.wishful_budget_share > 0.
                        || (full_at % lattice == 0.
                            && stocks.wish_ready_at % crate::constants::TICK_MS == 0.);
                    let trimmed_spend = trimmed
                        && (if def.charges == 1 {
                            full_at <= spend.at + recharge && full_at >= spend.at + recharge - max_cut
                        } else {
                            (spend.at + recharge - max_cut..=spend.at + 2. * recharge)
                                .contains(&full_at)
                                && stocks.wish_ready_at + recharge >= full_at
                        })
                        && on_lattice;
                    let refunded = def.cast_ms > 0.
                        && stocks.wish_ready_at == full_at
                        && full_at <= spend.at
                        && prior_clock_any(full_at, initial, first_cast, spend.at, cooldowns, recharge)
                        && w.abilities
                            .as_ref()
                            .and_then(|a| a.teleported_at)
                            // Later teleports replace this source-global clock. It proves a
                            // subsequent teleport exists, not which teleport refunded the cast.
                            .is_some_and(|at| at >= spend.at);
                    if !spent && !refunded && !trimmed_spend {
                        return Err(
                            "Wish stock clocks lack a completed spend, teleport refund or card trim".into(),
                        );
                    }
                }
            }
        }
        if stocks.q_ready_at != 0. || stocks.e_ready_at != 0. {
            return Err("unequipped skill stock".into());
        }
    }
    for slot in 0..w.fighter_count() {
        let ix = crate::fighter::FighterIx::of_slot(slot);
        let p = w.core(ix);
        let Some(c) = p.statuses.cast else {
            continue;
        };
        let Some(d) = c.producer else {
            if full {
                return Err("bare cast in full producer mode".into());
            }
            continue;
        };
        if !full || !w.is_player(ix) {
            return Err("Wish producer outside player full mode".into());
        }
        let def = definition(c.ability);
        let stocks = w.players[ix.slot()].stocks;
        if c.ability != wish(w.players[ix.slot()].faction())
            || d.slot != Slot::Wish
            || d.rank != 1
            || c.cancelled_by != CANCELLED_BY
            || c.lands_at != c.started_at + def.cast_ms
            || def.cast_ms == 0.
        {
            return Err("incoherent Wish producer definition".into());
        }
        if ![d.point.x, d.point.y]
            .into_iter()
            .all(crate::status_runtime::geometry_scalar_is_safe)
            || !(0. ..=crate::constants::WORLD_W).contains(&d.point.x)
            || !(0. ..=crate::constants::WORLD_H).contains(&d.point.y)
            || !(crate::constants::TICK_MS..=crate::constants::MAX_REWIND_MS).contains(&d.lag_ms)
        {
            return Err("invalid Wish point or lag".into());
        }
        if matches!(def.target, Target::Unit(_)) != d.target.is_some()
            || d.target.is_some_and(|id| id == p.id || !w.known_actor(id))
        {
            return Err("invalid Wish target provenance".into());
        }
        // The cast's spend carries the cooldown it was accepted with; a grant while the cast is in flight changes
        // only the multiplier now (`wish_cooldowns`).
        let cooldowns = wish_cooldowns(k, c.ability, p.modifiers.wish_cooldown_mult());
        let spend = w.players[ix.slot()]
            .card_runtime
            .and_then(|c| c.wish_cast)
            .filter(|s| s.at == c.started_at && cooldowns.contains(&s.cooldown_ms));
        let recharge = spend.map_or(cooldowns[1], |s| s.cooldown_ms);
        if !d.previous_ready_at.is_finite()
            || d.previous_ready_at < 0.
            || d.previous_ready_at > c.started_at
            || d.previous_full_at != Some(d.previous_ready_at)
            || !prior_clock_any(
                d.previous_ready_at,
                initial,
                first_cast,
                c.started_at,
                cooldowns,
                recharge,
            )
        {
            return Err("invalid Wish rollback clock".into());
        }
        let (full_at, ready_at) = spend_charge(
            if def.charges > 1 {
                d.previous_full_at.unwrap_or(0.)
            } else {
                0.
            },
            c.started_at,
            def.charges,
            recharge,
        );
        if spend.is_none() {
            return Err("cast lacks canonical card spend".into());
        }
        if stocks.wish_full_at != Some(full_at) || stocks.wish_ready_at != ready_at {
            return Err("incoherent spent Wish clocks".into());
        }
        if w.phase != crate::Phase::Active
            || !w.standing(ix)
            || (c.lands_at <= w.server_time && !w.rt.voided)
        {
            return Err("Wish cast outside reachable completed-step phase".into());
        }
    }
    Ok(())
}

/// The actual player Blaze producer is the only admitted generic mob DoT in this slice.
pub(crate) fn blaze_dot(
    w: &crate::state::World,
    st: &crate::status_runtime::StatusInstance,
) -> bool {
    use crate::status_runtime as rt;
    let Some(owner) = st.source.id else {
        return false;
    };
    let Some((dps, tail)) = rt::rank_one_field_policy(AbilityId::Blaze).burn else {
        return false;
    };
    st.kind == rt::StatusKind::Dot
        && st.ability == Some(AbilityId::Blaze)
        && w.roster
            .label(owner)
            .is_some_and(|name| name == &st.source.name)
        && st.source.owner_id.is_none()
        && st.tag.is_none()
        && st.value == dps
        && st.until - st.started_at >= tail
        // Every real refresh sets until=now+tail and retains the original start.
        // Overlapping charges may keep refreshing beyond one trail's lifetime.
        && st.started_at <= w.server_time
        && st.until <= w.server_time + tail
        && w.player(owner).is_none_or(|p| p.faction() == crate::FactionId::Fire)
        && st.reveal_from.is_none()
        && st.taken.is_none()
        && st.data
            == rt::Payload {
                shares: Some(crate::content::DamageShares {
                    phys: 0.,
                    magic: 0.,
                    true_: 1.,
                }),
                ..Default::default()
            }
}

/// Blaze resets before the same tick's ordinary history append. The retained canonical spend
/// proves the reset timestamp; only the first pair can share a time. Ordinary movement
/// later in that tick can move its second position (the queued post-Cast Move source case).
pub(crate) fn blaze_history(k: &crate::statics::MatchStatic, p: &crate::state::Player) -> bool {
    let h = p.body.history.as_slice();
    k.config.abilities == Mode::Full
        && p.faction() == crate::FactionId::Fire
        && h.len() >= 2
        && h[0].t == h[1].t
        && p.card_runtime
            .and_then(|c| c.wish_cast)
            .is_some_and(|c| c.at == h[0].t)
}

#[cfg(test)]
mod wish_cooldown_lever_tests {
    use crate::cards::{CardId, CardRow, Tuning};
    use crate::stat_fold::{EffectKind, MultKey};

    /// `wish_cooldowns` (the restore's envelope of a spend's cooldown) holds while exactly one card, unique, carries a
    /// `WishCooldown` row and that row only lengthens, in both tables: a build that only grows then had either no
    /// multiplier or today's at every cast. A second lever, or one that shortens, needs a new envelope here.
    #[test]
    fn one_unique_card_only_lengthens_the_wish_cooldown_in_both_tables() {
        for augments in [crate::AugmentRule::Legacy, crate::AugmentRule::Rework] {
            let t = Tuning::of(&crate::SimConfig {
                augments,
                ..crate::SimConfig::default()
            });
            let mut levers = Vec::new();
            for id in CardId::ALL {
                let def = t.def(id);
                for row in def
                    .rows
                    .iter()
                    .chain(def.selections.iter().flat_map(|s| s.rows.iter()))
                {
                    if let CardRow::Fold(EffectKind::Mult {
                        key: MultKey::WishCooldown,
                        v,
                    }) = row
                    {
                        assert!(*v >= 1., "{augments:?} {id:?}: a Wish cooldown factor below 1");
                        levers.push(id);
                    }
                }
            }
            assert_eq!(levers, [CardId::ArcaneOverload], "{augments:?}");
            assert!(t.def(CardId::ArcaneOverload).unique);
        }
    }
}
