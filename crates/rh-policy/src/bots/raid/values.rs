//! The value table (P-RAID-V): camp-equivalents per quest reward, native augment, camp and elite tier. Holds `Prices`, the
//! one struct every executor prices with (`Cx::prices`), including the farm's death model and the chest pick. Local
//! constants until P-RAID-V's table; a caller swaps them with `RaidBot::with_prices`. No executor or arbiter file holds a
//! price of its own: a number that values or times a goal lives here.

/// How a camp fight's death chance is priced (`exec/farm.rs` `death_chance`). An aggroed camp fights together: while the
/// bot kills member k of n, the n - k + 1 still standing all hit it, so the hp a clear costs is the duel's loss times
/// n(n+1)/2 times `fight_weight`. The local numbers make a fresh fighter's three-member camp a small risk (HumanBot clears
/// those 8 of 8, P-RAID-0 oracle) and a five-member camp a likely death without sustain (32 of 96 HumanBot camps of all
/// sizes ended in death or no clear).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct DeathModel {
    /// Multiplier on the hp a clear costs (unitless): the reference duel is kinder than a real fight.
    pub fight_weight: f64,
    /// The death chance rises by this per unit of (hp needed / hp held) past `safe_ratio` (chance per ratio unit).
    pub death_per_ratio: f64,
    /// The ratio of hp needed to hp held up to which a fight costs only the floor chance (unitless).
    pub safe_ratio: f64,
    /// The member count one `Prices::camp` stands for (members; camps hold three to five).
    pub typical_members: f64,
}

/// The value inputs, in camp-equivalents (ce: one normal camp cleared = 12 stat stacks = 1). Local constants until
/// P-RAID-V (`data/raid_values.json`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Prices {
    /// One normal camp cleared (ce).
    pub camp: f64,
    /// Stat stacks one camp-equivalent stands for (stacks per ce, the workbook's unit).
    pub stacks_per_camp: f64,
    /// One reward chest's pick (ce).
    pub chest: f64,
    /// What roaming finds per minute on average (ce per minute: the floor every other goal must beat).
    pub roam_per_minute: f64,
    /// What dying loses: the run's progress and the time to get back to farming (ce).
    pub death_loss: f64,
    /// Seconds to kill one normal camp member, fighting it alone (s).
    pub kill_seconds: f64,
    /// The farm fight's death model.
    pub death: DeathModel,
    /// Seconds a chest pick takes once at the chest: the panel's birth hold and a click (s).
    pub pick_seconds: f64,
    /// The chance a walk to a chest ends in a pick; it may expire or be taken (0..=1).
    pub pick_success: f64,
    /// A quest reward's worth when the table has no row for its def id (ce; 1 is a placeholder until P-RAID-V).
    pub reward_default: f64,
    /// An augment's worth when the table has no row for the card (ce; 1 is a placeholder until P-RAID-V).
    pub augment_default: f64,
    /// Quest reward rows: (board def id, ce). A def with no row takes `reward_default`.
    pub reward_rows: &'static [(&'static str, f64)],
    /// Augment rows: (`CardId::as_str`, ce). A card with no row takes `augment_default`.
    pub augment_rows: &'static [(&'static str, f64)],
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
        reward_rows: &[],
        augment_rows: &[],
    };

    /// `LOCAL` with quest rewards and augments worth nothing: a seat that farms, roams and opens chests only, so a test
    /// of the farm executor is not changed by quest executors registered later. It zeroes `reward_default` and
    /// `augment_default` only (camps, chests, roaming and the death model keep `LOCAL`); a later packet that adds
    /// per-def or per-card rows must clear those rows here too, or `farm_only` stops turning the families off.
    pub const fn farm_only() -> Self {
        Self {
            reward_default: 0.,
            augment_default: 0.,
            ..Self::LOCAL
        }
    }

    /// Camp-equivalents a quest's reward is worth, by the quest row's def id (`goal::Target::Quest::def`). Every quest
    /// executor multiplies its candidate's value by this and reads a reward's worth nowhere else, so a table turns the
    /// whole quest family off by pricing it at zero (`farm_only` does). One flat `reward_default` until P-RAID-V adds
    /// per-def rows here.
    pub fn reward_value(&self, _def: &str) -> f64 {
        self.reward_default
    }

    /// Camp-equivalents an augment card is worth. Every executor that values an augment (elite chests, augment quests)
    /// multiplies by this and reads an augment's worth nowhere else, so a table turns augment hunting off by pricing it
    /// at zero (`farm_only` does). One flat `augment_default` until P-RAID-V adds per-card rows here.
    pub fn augment_value(&self, _card: &str) -> f64 {
        self.augment_default
    }
}

impl Default for Prices {
    fn default() -> Self {
        Self::LOCAL
    }
}
