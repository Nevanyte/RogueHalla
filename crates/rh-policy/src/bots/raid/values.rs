//! The value table (P-RAID-V): camp-equivalents per quest reward, native augment, camp and elite tier. Holds `Prices`, the
//! one struct every executor prices with (`Cx::prices`), including the death model the farm fight uses. Local constants
//! until P-RAID-V's table; a caller swaps them with `RaidBot::with_prices`.

/// How a camp fight's death chance is priced (`exec/farm.rs`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct DeathModel {
    /// The hp a clear costs is the duel's loss times n(n+1)/2 times this.
    pub fight_weight: f64,
    /// The death chance rises by this per unit of (hp needed / hp held) past `safe_ratio`.
    pub death_per_ratio: f64,
    pub safe_ratio: f64,
    /// The member count one `Prices::camp` stands for.
    pub typical_members: f64,
}

/// The value inputs, in camp-equivalents (ce). Local constants until P-RAID-V (`data/raid_values.json`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Prices {
    /// One normal camp cleared.
    pub camp: f64,
    /// Stat stacks one camp-equivalent stands for (the workbook's unit).
    pub stacks_per_camp: f64,
    /// One reward chest's pick.
    pub chest: f64,
    /// What roaming finds per minute on average (the floor every other goal must beat).
    pub roam_per_minute: f64,
    /// What dying loses: the run's progress and the time to get back to farming.
    pub death_loss: f64,
    /// Seconds to kill one normal camp member, fighting it alone.
    pub kill_seconds: f64,
    pub death: DeathModel,
    /// Seconds a chest pick takes once at the chest.
    pub pick_seconds: f64,
    /// The chance a walk to a chest ends in a pick.
    pub pick_success: f64,
    /// A quest reward's worth when the table has no row for its def id (placeholder until P-RAID-V).
    pub reward_default: f64,
    /// An augment's worth when the table has no row for the card (placeholder until P-RAID-V).
    pub augment_default: f64,
}

impl Prices {
    pub const LOCAL: Self = Self {
        camp: 1.,
        stacks_per_camp: 12.,
        chest: 1.5,
        roam_per_minute: 0.1,
        death_loss: 6.,
        kill_seconds: 10.,
        death: DeathModel {
            fight_weight: 3.,
            death_per_ratio: 0.4,
            safe_ratio: 0.65,
            typical_members: 4.,
        },
        pick_seconds: 3.,
        pick_success: 0.9,
        reward_default: 1.,
        augment_default: 1.,
    };

    /// `LOCAL` with quest rewards and augments worth nothing: a seat that farms, roams and opens chests only, so a test
    /// of the farm executor is not changed by quest executors registered later.
    pub const fn farm_only() -> Self {
        Self {
            reward_default: 0.,
            augment_default: 0.,
            ..Self::LOCAL
        }
    }

    /// Camp-equivalents a quest's reward is worth, by the quest row's def id. Every quest executor multiplies its
    /// candidate's value by this, and a table can set it to zero (`farm_only`). (Stub: the fields are not read yet.)
    pub fn reward_value(&self, _def: &str) -> f64 {
        1.
    }

    /// Camp-equivalents an augment card is worth. (Stub: the fields are not read yet.)
    pub fn augment_value(&self, _card: &str) -> f64 {
        1.
    }
}

impl Default for Prices {
    fn default() -> Self {
        Self::LOCAL
    }
}
