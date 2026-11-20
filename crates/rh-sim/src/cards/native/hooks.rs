//! P-AUG-NATIVE-1b: the native hook points. Existing systems call [`fire`] as a plain helper at 13 points (no schedule
//! row, no hook-chain link, no RNG draw); it reaches the holder's native slots through the batch tables' handlers
//! (`weapon.rs`, `zone.rs`, `faction.rs`), only under `AugmentRule::Rework` and only for a player holding a native card.
//! The call sites and their events are listed in `docs/backlog/packets/P-AUG-NATIVE-1b.md` section 1.
use crate::state::{PlayerId, World};
use crate::statics::MatchStatic;

/// The kind of a native hook point (one per plan name; the variant names are pinned by the oracle's source scan).
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub(crate) enum NativeHook {
    LongRangeHit,
    CreditedKill,
    FistHit,
    MobKill,
    HazardTick,
    WishCast,
    CorpseHarvest,
    BurstLanded,
    ChillInflicted,
    HuntStack,
    Block,
    Overheal,
    HitCount,
}

impl NativeHook {
    /// Every hook point, in the brief's order.
    #[allow(dead_code)] // read by the oracle (hooks_tests.rs) and by the batch tables' handler lookups
    pub(crate) const ALL: [NativeHook; 13] = [
        NativeHook::LongRangeHit,
        NativeHook::CreditedKill,
        NativeHook::FistHit,
        NativeHook::MobKill,
        NativeHook::HazardTick,
        NativeHook::WishCast,
        NativeHook::CorpseHarvest,
        NativeHook::BurstLanded,
        NativeHook::ChillInflicted,
        NativeHook::HuntStack,
        NativeHook::Block,
        NativeHook::Overheal,
        NativeHook::HitCount,
    ];
}

/// One native hook event with its payload (a proposal: the implementer may change the payloads, not the names).
#[allow(dead_code)] // stub (P-AUG-NATIVE-1b): the call sites the implementer adds construct every variant
#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) enum NativeEvent {
    /// The holder's projectile landed a hit after flying at least the long-range share of its launch range.
    LongRangeHit,
    /// The holder is the credited killer of a player (`player`) or a mob (`elite` when it was an elite).
    CreditedKill { player: bool, elite: bool },
    /// The holder landed a fist hit.
    FistHit,
    /// The holder is credited with a mob kill of this type (the card's marks decide whether it is the first).
    MobKill {
        mob_type: crate::mob_catalog::MobTypeId,
    },
    /// The holder spent this tick on a zone hazard (0 ember, 1 bog, 2 ice).
    HazardTick { hazard: u8, ms: f64 },
    /// The holder's Wish cast landed.
    WishCast,
    /// The holder consumed a plague corpse.
    CorpseHarvest,
    /// The holder's fully charged roaming burst landed.
    BurstLanded,
    /// The holder chilled another fighter for this long.
    ChillInflicted { ms: f64 },
    /// The holder's hunt paid this many stacks.
    HuntStack { stacks: u32 },
    /// The holder's shield blocked this packet (`HookArgs::Blocked`'s `prevented`).
    Block { prevented: crate::state::Packet },
    /// The holder was healed this much beyond its max hp.
    Overheal { amount: f64 },
    /// The holder landed a weapon hit on `victim` for `amount`.
    HitCount { victim: PlayerId, amount: f64 },
}

impl NativeEvent {
    /// The kind of this event.
    pub(crate) fn hook(&self) -> NativeHook {
        match self {
            NativeEvent::LongRangeHit => NativeHook::LongRangeHit,
            NativeEvent::CreditedKill { .. } => NativeHook::CreditedKill,
            NativeEvent::FistHit => NativeHook::FistHit,
            NativeEvent::MobKill { .. } => NativeHook::MobKill,
            NativeEvent::HazardTick { .. } => NativeHook::HazardTick,
            NativeEvent::WishCast => NativeHook::WishCast,
            NativeEvent::CorpseHarvest => NativeHook::CorpseHarvest,
            NativeEvent::BurstLanded => NativeHook::BurstLanded,
            NativeEvent::ChillInflicted { .. } => NativeHook::ChillInflicted,
            NativeEvent::HuntStack { .. } => NativeHook::HuntStack,
            NativeEvent::Block { .. } => NativeHook::Block,
            NativeEvent::Overheal { .. } => NativeHook::Overheal,
            NativeEvent::HitCount { .. } => NativeHook::HitCount,
        }
    }
}

/// Fires one native hook event for `holder`: nothing unless the world plays the `Rework` card table and the holder
/// holds a native card; then each held card's handler for this hook runs (the batch tables).
#[allow(dead_code)] // stub (P-AUG-NATIVE-1b): the implementer's call sites use it
pub(crate) fn fire(k: &MatchStatic, w: &mut World, holder: PlayerId, event: NativeEvent) {
    if k.cards.rule() != crate::AugmentRule::Rework {
        return;
    }
    let Some(p) = w.player(holder) else { return };
    if p.build.native.is_empty() {
        return;
    }
    let hook = event.hook();
    #[cfg(test)]
    trace::record(holder, hook);
    let _ = hook;
    // Stub: the handlers of the held cards (weapon.rs, zone.rs, faction.rs) run here.
}

/// Test-only record of every dispatch that passed the guard (thread-local: each test thread sees its own sims).
#[cfg(test)]
pub(crate) mod trace {
    use super::NativeHook;
    use crate::state::PlayerId;
    use std::cell::RefCell;

    thread_local! {
        static TRACE: RefCell<Vec<(PlayerId, NativeHook)>> = const { RefCell::new(Vec::new()) };
    }

    pub(crate) fn record(holder: PlayerId, hook: NativeHook) {
        TRACE.with(|t| t.borrow_mut().push((holder, hook)));
    }

    /// Takes (and clears) this thread's record.
    pub(crate) fn take() -> Vec<(PlayerId, NativeHook)> {
        TRACE.with(|t| std::mem::take(&mut *t.borrow_mut()))
    }
}
