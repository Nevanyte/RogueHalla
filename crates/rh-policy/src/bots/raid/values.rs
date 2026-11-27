//! The value table (P-RAID-V): camp-equivalents per quest reward, native augment, camp and elite tier. Holds `Prices`, the
//! one struct every executor prices with (`Cx::prices`), including the farm's death model and the chest pick. Local
//! constants (the reward and augment rows are the workbook estimates); a caller swaps them with `RaidBot::with_prices`. No executor or arbiter file holds a
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

/// What `exec/sustain.rs` prices with (P-RAID-C): when to drink, when a fight is lost, how much hp it wants before
/// it takes a camp. Shares are of max hp. `OFF` turns the executor off (it proposes nothing, is never urgent, draws
/// nothing): `Prices::farm_only` uses it so the farm's own tests replay as before.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SustainPrices {
    /// The executor is on.
    pub on: bool,
    /// Drink a potion when hp is below this share (legacy `shouldUsePotion`: 0.65).
    pub drink_below: f64,
    /// Leave a camp fight once the death model's chance to die finishing it, from the hp held and the members
    /// still alive, is at least this (0..=1).
    pub disengage_death: f64,
    /// Leave this much early (chance): the percept's hp and member counts are rounded and a frame old, so a leave decided
    /// at exactly `disengage_death` comes late.
    pub disengage_lead: f64,
    /// The hp share the heal-up candidate brings the bot back to before it goes for a camp.
    pub ready_hp: f64,
    /// Share of max hp a large potion gives back (the sim's `potions::DEFINITION.large_share`): the farm prices a camp
    /// with the potions held counted as hp.
    pub large_share: f64,
    /// Share of max hp a small potion gives back (`potions::DEFINITION.small_share`).
    pub small_share: f64,
    /// What leaving a lost fight is worth (ce): the death it avoids, priced so a leave outranks any camp.
    pub leave_value: f64,
    /// Seconds a leave takes to price (s): short, so the leave's rate is high.
    pub leave_seconds: f64,
    /// Worth of one share of max hp healed on purpose (ce per share): with `regen_per_second` this fixes the heal-up's
    /// rate in ce per minute, which a camp worth taking beats and standing still does not.
    pub rest_value: f64,
    /// Seconds a potion takes to give its share back (`potions::DEFINITION.heal_ms`): hp still to come while a heal runs.
    pub heal_seconds: f64,
    /// A fight is on while the bot lost hp within this many seconds, or a member stands within reach of it on a camp it
    /// works: a camp passed at a distance is not a fight to leave.
    pub hurt_seconds: f64,
    /// Share of max hp the sim regenerates a second after the quiet spell (`PLAYER_REGEN_SHARE_OUT`).
    pub regen_per_second: f64,
    /// A stand ends once hp is this share of max hp below the most it held since the stand began (a fall from a source the
    /// hold does not see; the sim regenerates 0.008 a second, so 0.03 is four seconds of regeneration lost at once).
    pub fall_share: f64,
}

impl SustainPrices {
    /// Hp share the potions held will give back (0 when sustain is off, so the farm prices as before).
    pub fn potion_hp(&self, large: u8, small: u8) -> f64 {
        if !self.on {
            return 0.;
        }
        f64::from(large) * self.large_share + f64::from(small) * self.small_share
    }

    pub const OFF: Self = Self {
        on: false,
        drink_below: 0.,
        disengage_death: 2.,
        disengage_lead: 0.,
        ready_hp: 0.,
        large_share: 0.,
        small_share: 0.,
        leave_value: 0.,
        leave_seconds: 1.,
        rest_value: 0.,
        regen_per_second: 0.008,
        heal_seconds: 5.,
        hurt_seconds: 3.,
        fall_share: 0.03,
    };
    pub const LOCAL: Self = Self {
        on: true,
        drink_below: 0.65,
        disengage_death: 0.5,
        disengage_lead: 0.15,
        ready_hp: 0.85,
        large_share: 0.4,
        small_share: 0.25,
        leave_value: 3.,
        leave_seconds: 6.,
        rest_value: 0.6,
        regen_per_second: 0.008,
        heal_seconds: 5.,
        hurt_seconds: 3.,
        fall_share: 0.03,
    };
}

/// What `exec/elite.rs` prices an elite camp with (P-RAID-E). `OFF` turns the executor off (it proposes nothing, picks
/// nothing, draws nothing): `Prices::farm_only` uses it so the pinned replays do not move. The death prior is the elite
/// bench's table (`tools/balance-camps/elite_after.txt`, duelist, Rework elites hp 1.5 dmg 1.0): it goes stale when
/// P-BAL-NUM-ELITE-1 changes the per-kind numbers (refresh `kind_death` and `weapon_factor` then).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ElitePrices {
    /// The executor is on.
    pub on: bool,
    /// What the kill itself pays (ce): the 12 stacks every contributor gets (`REWORK_ELITE_SHARE_STACKS`).
    pub share: f64,
    /// What the chest's best card is worth before the offer is seen (ce): the mean best of three over `augment_rows`.
    pub pool_pick: f64,
    /// The banked-camp counts of the `kind_death` columns (camps of `Prices::stacks_per_camp` stacks).
    pub camp_columns: [f64; 4],
    /// Death rate of the reference fighter per elite kind (`MobTypeId::as_str`) at `camp_columns` banked camps (0..=1).
    pub kind_death: &'static [(&'static str, [f64; 4])],
    /// Death-rate factor per weapon in hand (`WeaponId::as_str`): the weapon's banked_12 rate over the all-weapon rate.
    pub weapon_factor: &'static [(&'static str, f64)],
    /// How long an elite fight takes from the first blow to the kill (s; measured: executioner at 36 stacks 47 to 73).
    pub fight_seconds: f64,
    /// Walk to the chest and pick once the elite is dead (s).
    pub chest_seconds: f64,
    /// The longest an elite fight may run before the goal gives the camp up (s).
    pub give_up_seconds: f64,
    /// How much of an elite one of its normal members weighs in the fallback death model (unitless).
    pub add_weight: f64,
    /// The least and the greatest death chance an elite fight is priced at (chance, 0..=1); a kind with no
    /// `kind_death` row reads the greatest in `death_prior` (the executor prices it with the farm model instead).
    pub death_range: (f64, f64),
    /// Standing at the camp with neither the elite nor a member in sight this long fails the goal (s)...
    pub empty_seconds: f64,
    /// ...and a camp given up on (empty, a fight past `give_up_seconds`, no way there) is not proposed for this long (s).
    pub ban_seconds: f64,
    /// After the kill, the goal looks this long for the camp's chest before it ends (s): the drop is immediate.
    pub chest_find_seconds: f64,
    /// After the kill, the goal stands at the chest at most this long (s; the chest lasts 15, `lifetime_ms`), and never
    /// past the chest's own expiry.
    pub chest_wait_seconds: f64,
    /// The fight's own exchange (hp lost per camp hp destroyed, in elites' worth: the elite plus `add_hp` a member)
    /// prices the death once the bot has destroyed this much...
    pub tally_min_dealt: f64,
    /// ...and lost this share of its own (shares of max hp).
    pub tally_min_lost: f64,
    /// A camp member's max hp as a share of its elite's (Rework: `MOB_K` 0.22 x 2.0 against `ELITE_K` 0.65 x 1.5,
    /// `constants.rs`): the fight's tally counts the adds destroyed in elites' worth.
    pub add_hp: f64,
    /// The share of fights the RaidBot ends with the elite dead inside `give_up_seconds`, by kind (`MobTypeId::as_str`),
    /// where measured; a kind with no row is 1 (the bench's duelist cleared what it did not die to). Measured in the
    /// `tests/raid_elite.rs` harness (sword, Holy, seed1 fixture map, seeds 1 to 6, the take forced by `death_loss` 0):
    /// executioner 6 of 6 (36 stacks, 2 potions); Blood Saint 0 of 12 (0 stacks and 36 stacks with 2 potions: it
    /// moves faster than a melee lock follows, its hp fell 0.35 in 120 s, and no fight was lost either).
    pub kind_clear: &'static [(&'static str, f64)],
    /// Each further give-up at one camp multiplies its ban by this (`ban_seconds` x growth^(n - 1)); 1 keeps the ban flat.
    pub ban_growth: f64,
    /// The longest ban a camp's give-ups grow to (s).
    pub ban_max_seconds: f64,
}

impl ElitePrices {
    pub const OFF: Self = Self {
        on: false,
        share: 0.,
        pool_pick: 0.,
        camp_columns: [0., 6., 12., 18.],
        kind_death: &[],
        weapon_factor: &[],
        fight_seconds: 60.,
        chest_seconds: 10.,
        give_up_seconds: 120.,
        add_weight: 0.25,
        death_range: (0.02, 0.98),
        empty_seconds: 3.,
        ban_seconds: 20.,
        chest_find_seconds: 3.,
        chest_wait_seconds: 16.,
        tally_min_dealt: 0.1,
        tally_min_lost: 0.1,
        add_hp: 0.45,
        kind_clear: &[],
        ban_growth: 1.,
        ban_max_seconds: 20.,
    };
    pub const LOCAL: Self = Self {
        on: true,
        share: 1.,
        pool_pick: 2.6,
        camp_columns: [0., 6., 12., 18.],
        kind_death: &[
            ("wyrmling", [0.833, 0.667, 0.256, 0.067]),
            ("ashDrake", [0.789, 0.422, 0.222, 0.122]),
            ("executioner", [0.656, 0.089, 0.022, 0.011]),
            ("gaoler", [0.933, 0.733, 0.233, 0.033]),
            ("ratOgre", [0.800, 0.333, 0.089, 0.056]),
            ("poxBehemoth", [0.533, 0.200, 0.144, 0.111]),
            ("glacialWarden", [0.811, 0.611, 0.178, 0.011]),
            ("avalancheTroll", [0.933, 0.878, 0.467, 0.200]),
            ("bloodSaint", [0.122, 0.078, 0.067, 0.067]),
            ("archon", [0.500, 0.100, 0.067, 0.056]),
        ],
        weapon_factor: &[
            ("sword", 0.49),
            ("bow", 2.68),
            ("wand", 1.42),
            ("shield", 0.38),
            ("axe", 0.34),
            ("fists", 0.81),
        ],
        fight_seconds: 50.,
        chest_seconds: 10.,
        give_up_seconds: 120.,
        add_weight: 0.25,
        death_range: (0.02, 0.98),
        empty_seconds: 3.,
        ban_seconds: 20.,
        chest_find_seconds: 3.,
        chest_wait_seconds: 16.,
        tally_min_dealt: 0.1,
        tally_min_lost: 0.1,
        add_hp: 0.45,
        kind_clear: &[("executioner", 1.), ("bloodSaint", 0.)],
        ban_growth: 2.,
        ban_max_seconds: 300.,
    };
}

/// The value inputs, in camp-equivalents (ce: one normal camp cleared = 12 stat stacks = 1). The reward and augment rows
/// are workbook estimates, mirrored in `data/raid_values.json`; lab measurements will replace them.
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
    /// A quest reward's worth when the table has no row for its def id (ce; 1 is a placeholder, every board def has a row).
    pub reward_default: f64,
    /// An augment's worth when the table has no row for the card (ce; 1 is a placeholder, every pool card has a row).
    pub augment_default: f64,
    /// Quest reward rows: (board def id, ce). A def with no row takes `reward_default`.
    pub reward_rows: &'static [(&'static str, f64)],
    /// Augment rows: (`CardId::as_str`, ce). A card with no row takes `augment_default`.
    pub augment_rows: &'static [(&'static str, f64)],
    /// Sustain (P-RAID-C): potions, disengaging, healing up.
    pub sustain: SustainPrices,
    /// What the spare weapon is worth when The Bargain takes it in trade (ce; a placeholder, no table row).
    pub spare_weapon: f64,
    /// What one stack of a stat nothing favours is worth in the Bargain's units (a chest boost's base, `choice.rs`).
    pub neutral_stat: f64,
    /// How the weapon in hand moves a stat's worth (the legacy chest fit, `choice.rs` `base_fit`).
    pub stat_fit: StatFit,
    /// The least chance a quest walk to die is priced at (chance, 0..=1).
    pub death_floor: f64,
    /// The minutes a decision already waiting costs to answer (min): what the arbiter sees.
    pub answer_minutes: f64,
    /// The share of its worth a reusable quest spell keeps while it has no charge (recharging).
    pub recharging_share: f64,
    /// The minutes from a Bargain's giver to its hag, priced before the quest is accepted (min; the hag is not in the view then).
    pub bargain_leg_minutes: f64,
    /// Trade Up (P-RAID-Q3b): each stage's share of the whole reward row, banked at one, two and three barters (the sim's
    /// `trade_up_stacks` over its last: 10, 22, 29 of 29). Strictly increasing, the last at most 1.
    pub trade_up_stage_share: [f64; 3],
    /// A committed quest walk (Two Banners, Trade Up) is priced at most this share of Sustain's leave rate
    /// (`leave_value` over `leave_seconds`), so a leave from a lost fight outranks the walk wherever the seat stands
    /// (0 < share < 1; unused while sustain is off).
    pub quest_hold_share: f64,
    /// What a quest's own objective does by design (P-RAID-Q3b-fix): (board def id, the share of attempts that end in a
    /// death at full health, 0..=0.98). A quest with no row takes `death_floor`. Measured, hand-edited in a reviewed packet
    /// after a lab run; not mirrored in `data/raid_values.json` (`values_tests.rs` pins only the reward and augment rows).
    pub quest_death_rows: &'static [(&'static str, f64)],
    /// Elite camps (P-RAID-E): what an elite and its chest are worth and how likely the fight kills the bot.
    pub elite: ElitePrices,
}

/// What the weapon in hand adds to a stat's worth (`bots/temporal_percept/modules/choice.rs` `base_fit`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StatFit {
    /// The stat suits the weapon (attack damage for a sword, axe or bow; ability power for a wand; armor and shield for a shield).
    pub matched: f64,
    /// Bare fists take attack damage and ability power equally.
    pub fists: f64,
    /// Attack damage for a wand.
    pub wand_attack: f64,
    /// The stat does not suit the weapon.
    pub mismatched: f64,
}

impl StatFit {
    pub const LOCAL: Self = Self {
        matched: 1.,
        fists: 0.5,
        wand_attack: -0.5,
        mismatched: -1.,
    };
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
        // Workbook estimates of 2026-11-22 (camp-equivalents, to be replaced by lab measurements). Mirrored in
        // data/raid_values.json; values_tests.rs pins both copies.
        reward_rows: &[
            ("the_warden", 2.0),
            ("hold_the_fort", 3.6),
            ("bounty_hunter", 3.5),
            ("smugglers_run", 2.5),
            ("two_banners", 3.0),
            ("ember_walk", 2.0),
            ("the_burning_crown", 5.2),
            ("let_him_cook", 3.7),
            ("elite_slayer", 2.1),
            ("tuber_eats", 2.2),
            ("barrow_run", 1.5),
            ("the_bargain", 2.7), // a switch: the candidate is the chosen trade's gain, not this
            ("a_cruel_joke", 2.0),
            ("a_bone_to_pick", 1.8),
            ("feed_or_hunt", 1.5),
            ("rime_hunt", 3.5),
            ("watchtowers", 0.9),
            ("whispers_in_the_brush", 0.8),
            ("even_skeletons_get_cold", 1.5),
            ("lost_brother", 2.5),
            ("pilgrims_path", 2.0),
            ("gatecrasher", 2.5),
            ("letters", 2.0),
            ("stay_a_while", 2.0),
            ("trade_up", 3.0),
        ],
        augment_rows: &[
            ("markedMan", 7.0),
            ("pathfinder", 3.2),
            ("marksmansTally", 2.4),
            ("eighthStrike", 1.9),
            ("calluses", 1.3),
            ("bastion", 1.2),
            ("hazardWalker", 1.1),
            ("glacier", 0.9),
            ("stoked", 0.9),
            ("overflow", 0.8),
            ("corpseHoarder", 0.6),
            ("wishEngine", 0.5),
            ("trophyHunter", 0.5),
            ("executionersLedger", 0.45),
            ("committed", 5.4),
            ("machineGun", 2.5),
            ("plantYourFeet", 2.3),
            ("fistsOfFury", 1.9),
            ("tankEngine", 1.9),
            ("gambler", 1.9),
            ("hereComesTheBoom", 1.7),
            ("hemorrhage", 1.5),
            ("overkill", 1.4),
            ("thirdTimesTheCharm", 0.9),
            ("spikedShell", 0.9),
            ("secondWind", 0.8),
            ("slowBurn", 0.6),
            ("bestOffense", 0.6),
            ("eyeForAnEye", 0.5),
            ("siphon", 0.5),
            ("wishfulThinking", 0.4),
            ("pocketSand", 0.3),
            ("raceToZero", 0.0),
        ],
        sustain: SustainPrices::LOCAL,
        spare_weapon: 1.5,
        neutral_stat: 2.5,
        stat_fit: StatFit::LOCAL,
        death_floor: 0.02,
        answer_minutes: 0.01,
        recharging_share: 0.5,
        bargain_leg_minutes: 0.5,
        trade_up_stage_share: [10. / 29., 22. / 29., 1.],
        quest_hold_share: 0.7,
        quest_death_rows: &[
            // Pilot of record (docs/evidence/balance-patch-2026-11/pilot/README.md, 40 matches, eight test bots): 7 attempts
            // of 189 ended in a death.
            ("let_him_cook", 7. / 189.),
            // The same pilot: 9 attempts of 32 (the hold is shown on every map and a player fight ends it).
            ("hold_the_fort", 9. / 32.),
        ],
        elite: ElitePrices::LOCAL,
    };

    /// `LOCAL` with quest rewards and augments worth nothing and sustain off: a seat that farms, roams and opens chests
    /// only, so a test of the farm executor is not changed by quest or sustain executors registered later. It zeroes
    /// `reward_default` and `augment_default`, clears both row tables and turns `sustain` off; camps, chests, roaming and
    /// the death model keep `LOCAL`, so every quest reward and augment is worth 0 here.
    pub const fn farm_only() -> Self {
        Self {
            reward_default: 0.,
            augment_default: 0.,
            reward_rows: &[],
            augment_rows: &[],
            sustain: SustainPrices::OFF,
            elite: ElitePrices::OFF,
            ..Self::LOCAL
        }
    }

    /// Camp-equivalents a quest's reward is worth, by the quest row's def id (`goal::Target::Quest::def`). Most quest
    /// executors multiply their candidate's value by this; a table turns the whole quest family off by pricing it at zero
    /// (`farm_only` does). The Bargain's row is a switch only (positive: on): its candidate is the chosen trade's gain,
    /// not capped by the row. The row for the def, else `reward_default`.
    pub fn reward_value(&self, def: &str) -> f64 {
        self.reward_rows
            .iter()
            .find(|(k, _)| *k == def)
            .map_or(self.reward_default, |(_, v)| *v)
    }

    /// The share of a quest's attempts that end in a death at full health: the row for the def, else `death_floor`.
    pub fn quest_death_share(&self, def: &str) -> f64 {
        self.quest_death_rows
            .iter()
            .find(|(k, _)| *k == def)
            .map_or(self.death_floor, |(_, v)| *v)
    }

    /// Camp-equivalents an augment card is worth. Every executor that values an augment (elite chests, augment quests)
    /// multiplies by this and reads an augment's worth nowhere else, so a table turns augment hunting off by pricing it
    /// at zero (`farm_only` does). The row for the card, else `augment_default`.
    pub fn augment_value(&self, card: &str) -> f64 {
        self.augment_rows
            .iter()
            .find(|(k, _)| *k == card)
            .map_or(self.augment_default, |(_, v)| *v)
    }
}

impl Default for Prices {
    fn default() -> Self {
        Self::LOCAL
    }
}
