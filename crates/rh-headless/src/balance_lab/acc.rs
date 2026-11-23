//! The counters `balance_lab` reports, one struct per report block. Every field is a sum, so two seeds merge by
//! adding (`Merge`), in seed order. Maps are `BTreeMap` (byte-stable output). Serialization order is declaration order.
use serde::ser::Serializer;
use std::collections::BTreeMap;

/// Addition of two accumulators of the same shape.
pub trait Merge {
    fn fold_in(&mut self, other: Self);
}
impl Merge for u64 {
    fn fold_in(&mut self, other: Self) {
        *self += other;
    }
}
impl<K: Ord, V: Merge> Merge for BTreeMap<K, V> {
    fn fold_in(&mut self, other: Self) {
        for (k, v) in other {
            match self.entry(k) {
                std::collections::btree_map::Entry::Occupied(mut e) => e.get_mut().fold_in(v),
                std::collections::btree_map::Entry::Vacant(e) => {
                    e.insert(v);
                }
            }
        }
    }
}

/// A sum of seconds and the number of samples in it (divide for a mean; `n` is the sample count).
#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Sum {
    pub sum: f64,
    pub n: u64,
}
impl Sum {
    pub fn add(&mut self, v: f64) {
        self.sum += v;
        self.n += 1;
    }
}
impl Merge for Sum {
    fn fold_in(&mut self, o: Self) {
        self.sum += o.sum;
        self.n += o.n;
    }
}

/// [`Sum`] that keeps its samples, in the order they were added and folded (seed order).
#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct SampledSum {
    pub sum: f64,
    pub n: u64,
    pub samples: Vec<f64>,
}
impl SampledSum {
    pub fn add(&mut self, v: f64) {
        self.sum += v;
        self.n += 1;
        self.samples.push(v);
    }
}
impl Merge for SampledSum {
    fn fold_in(&mut self, o: Self) {
        self.sum += o.sum;
        self.n += o.n;
        self.samples.extend(o.samples);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Causes {
    pub player: u64,
    pub elite: u64,
    pub mob: u64,
    pub hazard: u64,
}
impl Merge for Causes {
    fn fold_in(&mut self, o: Self) {
        self.player += o.player;
        self.elite += o.elite;
        self.mob += o.mob;
        self.hazard += o.hazard;
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct MinuteDeaths {
    /// Standing-player ticks in `Active` phase this minute (divide by 1200 for player-minutes).
    pub player_ticks: u64,
    pub total: u64,
    pub by_cause: Causes,
}
impl Merge for MinuteDeaths {
    fn fold_in(&mut self, o: Self) {
        self.player_ticks += o.player_ticks;
        self.total += o.total;
        self.by_cause.fold_in(o.by_cause);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Deaths {
    pub total: u64,
    pub by_cause: Causes,
    pub by_minute: BTreeMap<u32, MinuteDeaths>,
    /// Hits on players, and those whose `Hit.by` resolved to a mob id: the check behind `elite`/`mob` vs `hazard`.
    pub hits_on_players: u64,
    pub hits_on_players_by_mob: u64,
}
impl Merge for Deaths {
    fn fold_in(&mut self, o: Self) {
        self.total += o.total;
        self.by_cause.fold_in(o.by_cause);
        self.by_minute.fold_in(o.by_minute);
        self.hits_on_players += o.hits_on_players;
        self.hits_on_players_by_mob += o.hits_on_players_by_mob;
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct NativeAcc {
    pub holders: u64,
    pub value_sum: f64,
    pub max_sum: f64,
}
impl Merge for NativeAcc {
    fn fold_in(&mut self, o: Self) {
        self.holders += o.holders;
        self.value_sum += o.value_sum;
        self.max_sum += o.max_sum;
    }
}

/// One of the minute samples (5, 8, 12): `samples` is the number of standing players read, which is what to divide by.
#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct MinuteAcc {
    pub samples: u64,
    pub farm_stacks: BTreeMap<String, u64>,
    pub boosts: BTreeMap<String, u64>,
    pub native: BTreeMap<String, NativeAcc>,
}
impl Merge for MinuteAcc {
    fn fold_in(&mut self, o: Self) {
        self.samples += o.samples;
        self.farm_stacks.fold_in(o.farm_stacks);
        self.boosts.fold_in(o.boosts);
        self.native.fold_in(o.native);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct World {
    pub camps_cleared: u64,
    /// Cleared camps with a credited seat (the seat with the most damage on the camp's mobs).
    pub camps_credited: u64,
    pub camps_by_type: BTreeMap<String, u64>,
    /// Cleared camps that held an elite (a subset of `camps_cleared`).
    pub elite_camps_cleared: u64,
    /// Heuristic: first hit on the camp by the credited seat to the clear.
    pub kill_secs: Sum,
    /// Heuristic: the clear until hp >= 95% or the next hit taken.
    pub heal_secs: Sum,
    /// Heuristic: end of heal-up to the seat's next hit on any mob.
    pub walk_secs: Sum,
    pub player_deaths: Deaths,
    pub minutes: BTreeMap<u32, MinuteAcc>,
}
impl Merge for World {
    fn fold_in(&mut self, o: Self) {
        self.camps_cleared += o.camps_cleared;
        self.camps_credited += o.camps_credited;
        self.camps_by_type.fold_in(o.camps_by_type);
        self.elite_camps_cleared += o.elite_camps_cleared;
        self.kill_secs.fold_in(o.kill_secs);
        self.heal_secs.fold_in(o.heal_secs);
        self.walk_secs.fold_in(o.walk_secs);
        self.player_deaths.fold_in(o.player_deaths);
        self.minutes.fold_in(o.minutes);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct EliteCounts {
    pub attempts: u64,
    pub killed: u64,
    pub died: u64,
    pub taken_by_other: u64,
    pub open: u64,
    /// The seat died in the fight's window to something that is not the elite or its camp.
    pub died_elsewhere: u64,
    /// Gave up: idle (no hit dealt or taken against the elite or its camp) for more than `LEAVE_MS`.
    pub left: u64,
    /// Contact to kill, one sample per `killed`.
    pub kill_secs: SampledSum,
    /// One chest outcome per kill: picked by the killing blow's seat, only by another seat, by nobody, or still standing.
    pub chest_killer: u64,
    pub chest_other: u64,
    pub chest_none: u64,
    pub chest_open: u64,
    /// Picks by seats other than the killer from the kills' chests (counted when the chest is gone).
    pub chest_bystander_picks: u64,
}
impl Merge for EliteCounts {
    fn fold_in(&mut self, o: Self) {
        self.attempts += o.attempts;
        self.killed += o.killed;
        self.died += o.died;
        self.taken_by_other += o.taken_by_other;
        self.open += o.open;
        self.died_elsewhere += o.died_elsewhere;
        self.left += o.left;
        self.kill_secs.fold_in(o.kill_secs);
        self.chest_killer += o.chest_killer;
        self.chest_other += o.chest_other;
        self.chest_none += o.chest_none;
        self.chest_open += o.chest_open;
        self.chest_bystander_picks += o.chest_bystander_picks;
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Elites {
    #[serde(serialize_with = "by_kind_weapon")]
    pub by_kind_weapon: BTreeMap<(String, String), EliteCounts>,
}
impl Merge for Elites {
    fn fold_in(&mut self, o: Self) {
        self.by_kind_weapon.fold_in(o.by_kind_weapon);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct QuestCounts {
    pub dealt: u64,
    pub accepted: u64,
    pub attempts: u64,
    pub completed: u64,
    pub ended_timer: u64,
    pub ended_death: u64,
    /// Ended otherwise: abandon, departure or anything the state cannot tell apart (the sim's probe cannot split them).
    pub abandoned: u64,
    /// Still accepted and not turned in at the last read.
    pub open: u64,
    /// Turned in at the last read and never paid.
    pub turned_in_unpaid: u64,
    /// Accept to turn-in, one sample a completion.
    pub complete_secs: SampledSum,
}
impl Merge for QuestCounts {
    fn fold_in(&mut self, o: Self) {
        self.dealt += o.dealt;
        self.accepted += o.accepted;
        self.attempts += o.attempts;
        self.completed += o.completed;
        self.ended_timer += o.ended_timer;
        self.ended_death += o.ended_death;
        self.abandoned += o.abandoned;
        self.open += o.open;
        self.turned_in_unpaid += o.turned_in_unpaid;
        self.complete_secs.fold_in(o.complete_secs);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Quests {
    #[serde(serialize_with = "by_quest")]
    pub by_quest: BTreeMap<String, QuestCounts>,
    /// First acceptance of an offer by minute of the match (tick / 1200).
    pub accepted_by_minute: BTreeMap<u32, u64>,
    /// Quest-spell casts by reward index (a drop in `charges`, see the README).
    pub spell_casts: BTreeMap<u8, u64>,
}
impl Merge for Quests {
    fn fold_in(&mut self, o: Self) {
        self.by_quest.fold_in(o.by_quest);
        self.accepted_by_minute.fold_in(o.accepted_by_minute);
        self.spell_casts.fold_in(o.spell_casts);
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Rewards {
    /// (seat, chest) pairs seen with `can_choose`.
    pub chests_offered: u64,
    /// Picks seen in observations (an option with `taken_by` the viewer), by option kind.
    pub picks_by_kind: BTreeMap<String, u64>,
    /// `Event::ChestTaken` count: the cross-check of the observed picks.
    pub chest_taken_events: u64,
    /// The same picks as `picks_by_kind`, by the chest they came from: `elite` (a kill's chest), `trophy`, `ground`.
    pub picks_by_chest_kind: BTreeMap<String, u64>,
    /// `ChestTaken` events whose option the lab could not name (a skip, or a pick no observation showed).
    pub picks_unnamed: u64,
}
impl Merge for Rewards {
    fn fold_in(&mut self, o: Self) {
        self.chests_offered += o.chests_offered;
        self.picks_by_kind.fold_in(o.picks_by_kind);
        self.chest_taken_events += o.chest_taken_events;
        self.picks_by_chest_kind.fold_in(o.picks_by_chest_kind);
        self.picks_unnamed += o.picks_unnamed;
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct AugCounts {
    pub offered: u64,
    pub picked: u64,
    pub held_by_winner: u64,
    /// Picks from an elite kill's chest by the killing blow's seat, and by another seat.
    pub picked_by_killer: u64,
    pub picked_by_bystander: u64,
}
impl Merge for AugCounts {
    fn fold_in(&mut self, o: Self) {
        self.offered += o.offered;
        self.picked += o.picked;
        self.held_by_winner += o.held_by_winner;
        self.picked_by_killer += o.picked_by_killer;
        self.picked_by_bystander += o.picked_by_bystander;
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
pub struct Augments {
    pub winners: u64,
    pub draws: u64,
    #[serde(serialize_with = "by_card")]
    pub by_card: BTreeMap<String, AugCounts>,
}
impl Merge for Augments {
    fn fold_in(&mut self, o: Self) {
        self.winners += o.winners;
        self.draws += o.draws;
        self.by_card.fold_in(o.by_card);
    }
}

/// What one seed contributes to the report.
#[derive(Clone, Debug, Default)]
pub struct Acc {
    pub world: World,
    pub elites: Elites,
    pub quests: Quests,
    pub rewards: Rewards,
    pub augments: Augments,
}
impl Merge for Acc {
    fn fold_in(&mut self, o: Self) {
        self.world.fold_in(o.world);
        self.elites.fold_in(o.elites);
        self.quests.fold_in(o.quests);
        self.rewards.fold_in(o.rewards);
        self.augments.fold_in(o.augments);
    }
}

#[derive(serde::Serialize)]
struct Quest<'a> {
    quest: &'a str,
    #[serde(flatten)]
    counts: &'a QuestCounts,
}
#[derive(serde::Serialize)]
struct Card<'a> {
    card: &'a str,
    #[serde(flatten)]
    counts: &'a AugCounts,
}
#[derive(serde::Serialize)]
struct Elite<'a> {
    kind: &'a str,
    weapon: &'a str,
    #[serde(flatten)]
    counts: &'a EliteCounts,
}

fn by_quest<S: Serializer>(m: &BTreeMap<String, QuestCounts>, s: S) -> Result<S::Ok, S::Error> {
    s.collect_seq(m.iter().map(|(quest, counts)| Quest { quest, counts }))
}
fn by_card<S: Serializer>(m: &BTreeMap<String, AugCounts>, s: S) -> Result<S::Ok, S::Error> {
    s.collect_seq(m.iter().map(|(card, counts)| Card { card, counts }))
}
fn by_kind_weapon<S: Serializer>(
    m: &BTreeMap<(String, String), EliteCounts>,
    s: S,
) -> Result<S::Ok, S::Error> {
    s.collect_seq(m.iter().map(|((kind, weapon), counts)| Elite {
        kind,
        weapon,
        counts,
    }))
}
