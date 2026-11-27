//! Player-facing definitions for the approved Top 25 quest board.

use crate::FactionId;

#[derive(
    Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, serde::Serialize, serde::Deserialize,
)]
pub enum QuestId {
    TheWarden,
    HoldTheFort,
    BountyHunter,
    SmugglersRun,
    TwoBanners,
    EmberWalk,
    TheBurningCrown,
    LetHimCook,
    EliteSlayer,
    TuberEats,
    BarrowRun,
    TheBargain,
    ACruelJoke,
    ABoneToPick,
    FeedOrHunt,
    RimeHunt,
    Watchtowers,
    WhispersInTheBrush,
    EvenSkeletonsGetCold,
    LostBrother,
    PilgrimsPath,
    Gatecrasher,
    Letters,
    StayAWhile,
    TradeUp,
}

impl QuestId {
    pub const ALL: [Self; 25] = [
        Self::TheWarden,
        Self::HoldTheFort,
        Self::BountyHunter,
        Self::SmugglersRun,
        Self::TwoBanners,
        Self::EmberWalk,
        Self::TheBurningCrown,
        Self::LetHimCook,
        Self::EliteSlayer,
        Self::TuberEats,
        Self::BarrowRun,
        Self::TheBargain,
        Self::ACruelJoke,
        Self::ABoneToPick,
        Self::FeedOrHunt,
        Self::RimeHunt,
        Self::Watchtowers,
        Self::WhispersInTheBrush,
        Self::EvenSkeletonsGetCold,
        Self::LostBrother,
        Self::PilgrimsPath,
        Self::Gatecrasher,
        Self::Letters,
        Self::StayAWhile,
        Self::TradeUp,
    ];

    pub const fn home(self) -> FactionId {
        match self {
            Self::TheWarden
            | Self::HoldTheFort
            | Self::BountyHunter
            | Self::SmugglersRun
            | Self::TwoBanners => FactionId::Sentenced,
            Self::EmberWalk
            | Self::TheBurningCrown
            | Self::LetHimCook
            | Self::EliteSlayer
            | Self::TuberEats => FactionId::Fire,
            Self::BarrowRun
            | Self::TheBargain
            | Self::ACruelJoke
            | Self::ABoneToPick
            | Self::FeedOrHunt => FactionId::Plague,
            Self::RimeHunt
            | Self::Watchtowers
            | Self::WhispersInTheBrush
            | Self::EvenSkeletonsGetCold
            | Self::LostBrother => FactionId::Frost,
            Self::PilgrimsPath
            | Self::Gatecrasher
            | Self::Letters
            | Self::StayAWhile
            | Self::TradeUp => FactionId::Holy,
        }
    }

    pub const fn definition(self) -> &'static BoardDef {
        &DEFINITIONS[self as usize]
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum RewardId {
    WardensCage,
    CrownPower,
    Hook,
    ShoulderCharge,
    Tornado,
    Turret,
    RebirthEgg,
    Warhorn,
    Smite,
    SpellShield,
    TheBargain,
    TradeUp,
    ForgetMeNotSeed,
    SwapStick,
    ChefsKiss,
    LanternWard,
    Cleanse,
    ScoutFalcon,
    Stasis,
    Bolas,
    WallBuster,
    BlindRageAndStillMind,
    FeedOrHuntDamage,
    WarmCoat,
    BreakersHelm,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RewardKind {
    SingleUse,
    Reusable,
    Permanent,
}

impl RewardId {
    pub const fn kind(self) -> RewardKind {
        match self {
            Self::CrownPower
            | Self::TheBargain
            | Self::TradeUp
            | Self::ChefsKiss
            | Self::FeedOrHuntDamage
            | Self::WarmCoat
            | Self::BreakersHelm => RewardKind::Permanent,
            Self::Hook
            | Self::ShoulderCharge
            | Self::Tornado
            | Self::Turret
            | Self::Smite
            | Self::SpellShield
            | Self::LanternWard
            | Self::Cleanse
            | Self::ScoutFalcon
            | Self::Bolas => RewardKind::Reusable,
            _ => RewardKind::SingleUse,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ClockKind {
    Route,
    Dwell,
    Race,
    Event,
    Decision,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BoardDef {
    pub id: &'static str,
    pub name: &'static str,
    pub giver: &'static str,
    pub objective: &'static str,
    pub reward_text: &'static str,
    pub reward: RewardId,
    /// Workbook Z timer in milliseconds; absent when the clock is not a route timer.
    pub route_timer_ms: Option<u64>,
    pub objective_count: u8,
    pub clock: ClockKind,
}

macro_rules! def {
    ($id:literal, $name:literal, $giver:literal, $objective:literal, $reward_text:literal, $reward:ident, $timer:expr, $count:expr, $clock:ident) => {
        BoardDef {
            id: $id,
            name: $name,
            giver: $giver,
            objective: $objective,
            reward_text: $reward_text,
            reward: RewardId::$reward,
            route_timer_ms: $timer,
            objective_count: $count,
            clock: ClockKind::$clock,
        }
    };
}

pub const DEFINITIONS: [BoardDef; 25] = [
    def!(
        "the_warden",
        "The Warden",
        "Gallows quest giver",
        "Kill a huge-stat elite in the Gallows by fighting it across fences, which shots cross but bodies cannot.",
        "Warden's Cage: a six-second fence ring that stops bodies and melee, but not shots.",
        WardensCage,
        None,
        1,
        Race
    ),
    def!(
        "hold_the_fort",
        "Hold the Fort",
        "Gallows quest giver",
        "Hold a Gallows guard house for 35 seconds of uncontested occupation; your hold is shown on every map.",
        "Turret: hits players and mobs.",
        Turret,
        None,
        1,
        Dwell
    ),
    def!(
        "bounty_hunter",
        "Bounty Hunter",
        "Gallows quest giver",
        "Kill a player who has an active bounty; a living fill bot may be marked when no real bounty exists.",
        "Hook: pulls the first enemy hit.",
        Hook,
        None,
        1,
        Event
    ),
    def!(
        "smugglers_run",
        "Smuggler's Run",
        "Gallows quest giver",
        "Carry your parcel back to the giver while publicly marked on every map.",
        "Shoulder Charge: a ground dash that knocks back the first enemy hit.",
        ShoulderCharge,
        Some(53_000),
        1,
        Route
    ),
    def!(
        "two_banners",
        "Two Banners",
        "Gallows quest giver",
        "Choose one of two opposing banner deliveries between the Gallows giver and a rival outpost; your choice does not close either branch for others.",
        "Warhorn: taunts and gains a buff per enemy taunted.",
        Warhorn,
        None,
        1,
        Race
    ),
    def!(
        "ember_walk",
        "Ember Walk",
        "Scorch quest giver",
        "Collect three tokens on burning ember ground in the Scorch.",
        "Spell Shield: blocks the next spell.",
        SpellShield,
        Some(50_000),
        3,
        Route
    ),
    def!(
        "the_burning_crown",
        "The Burning Crown",
        "Scorch quest giver",
        "Carry the crown back to its giver; it grows your stats, burns you, is shown on every map, and drops on death.",
        "Crown power: stats accumulated while carrying.",
        CrownPower,
        None,
        1,
        Race
    ),
    def!(
        "let_him_cook",
        "Let Him Cook",
        "Scorch quest giver",
        "Bring an apple, wildberries, and turtle stew from three zones.",
        "Chef's Kiss: +20% to your lowest stat.",
        ChefsKiss,
        Some(95_000),
        3,
        Route
    ),
    def!(
        "elite_slayer",
        "Elite Slayer",
        "Scorch quest giver",
        "Kill an elite in the Scorch; each accepted player's progress is independent.",
        "Smite: bursts a mob and can steal kills.",
        Smite,
        None,
        1,
        Race
    ),
    def!(
        "tuber_eats",
        "Tuber Eats",
        "Scorch quest giver",
        "Deliver a rancid potato across the map.",
        "Breaker's Helm: 20% of axe and bow damage ignores armor.",
        BreakersHelm,
        Some(56_000),
        1,
        Route
    ),
    def!(
        "barrow_run",
        "Barrow Run",
        "Warren quest giver",
        "Tour the Warren barrows and the bored tunnel.",
        "WallBuster: destroys walls within 300 units.",
        WallBuster,
        Some(45_200),
        2,
        Route
    ),
    def!(
        "the_bargain",
        "The Bargain",
        "Warren quest giver",
        "Strike a bargain at the hag: pay stat stacks or a weapon.",
        "Convert 20 stacks into 28 of a stat you choose, or trade your spare weapon for a stat.",
        TheBargain,
        None,
        1,
        Decision
    ),
    def!(
        "a_cruel_joke",
        "A Cruel Joke",
        "Warren quest giver",
        "Scare three quest givers away; they flee for 60 seconds for you alone, then return.",
        "Swap-Stick: swaps weapons with a target for 10 seconds.",
        SwapStick,
        Some(50_000),
        3,
        Route
    ),
    def!(
        "a_bone_to_pick",
        "A Bone to Pick",
        "Warren quest giver",
        "Hunt a named skeleton archer who kites you across the Warren.",
        "Bolas: roots for 1.5 seconds; reusable with a cooldown.",
        Bolas,
        Some(53_000),
        1,
        Route
    ),
    def!(
        "feed_or_hunt",
        "Feed or Hunt",
        "Warren quest giver",
        "Feed a regional creature three zone items instead of hunting its kind.",
        "+6% damage as a zone stat.",
        FeedOrHuntDamage,
        Some(50_000),
        3,
        Route
    ),
    def!(
        "rime_hunt",
        "Rime Hunt",
        "Rime quest giver",
        "Clear one Frost camp of ranged mobs on the ice.",
        "Tornado: pulls, with strength that scales with max HP.",
        Tornado,
        Some(110_000),
        1,
        Route
    ),
    def!(
        "watchtowers",
        "Watchtowers",
        "Rime quest giver",
        "Visit the Rime watchtowers.",
        "Scout Falcon: a long reveal shot.",
        ScoutFalcon,
        Some(38_000),
        2,
        Route
    ),
    def!(
        "whispers_in_the_brush",
        "Whispers in the Brush",
        "Rime quest giver",
        "Find three tokens hidden in bushes; they appear only when you are close.",
        "Lantern Ward: reusable charges reveal a radius.",
        LanternWard,
        Some(35_000),
        3,
        Route
    ),
    def!(
        "even_skeletons_get_cold",
        "Even Skeletons Get Cold",
        "Rime quest giver",
        "Deliver a coat to a scout in the next zone.",
        "Warm Coat: immune to chill and bog slow.",
        WarmCoat,
        Some(45_200),
        1,
        Route
    ),
    def!(
        "lost_brother",
        "Lost Brother",
        "Rime quest giver",
        "Fetch a named relic from another zone.",
        "BlindRage and StillMind draughts: one of each, claimable separately.",
        BlindRageAndStillMind,
        Some(59_000),
        1,
        Route
    ),
    def!(
        "pilgrims_path",
        "Pilgrim's Path",
        "Sanctum quest giver",
        "Visit both Sanctum shrines and two light bays.",
        "Cleanse: removes roots, stuns, and pulls.",
        Cleanse,
        Some(56_000),
        4,
        Route
    ),
    def!(
        "gatecrasher",
        "Gatecrasher",
        "Sanctum quest giver",
        "Damage two distinct opponents during one coliseum lock and survive until the gates reopen.",
        "Rebirth Egg: plant it; after a real death, hatch there with 40% HP.",
        RebirthEgg,
        None,
        2,
        Event
    ),
    def!(
        "letters",
        "Letters",
        "Sanctum quest giver",
        "Carry a token between givers in opposite zones before the next coliseum lock; it expires at that lock and can be retried.",
        "Forget-Me-Not Seed: mark a spot and teleport back later.",
        ForgetMeNotSeed,
        None,
        1,
        Event
    ),
    def!(
        "stay_a_while",
        "Stay A While",
        "Sanctum quest giver",
        "Complete a 30-second uninterrupted meditation channel at the monk; any player can interrupt it.",
        "Stasis: 2.5 seconds invulnerable, but unable to act.",
        Stasis,
        None,
        1,
        Dwell
    ),
    def!(
        "trade_up",
        "Trade Up",
        "Sanctum quest giver",
        "Barter your parcel with three wandering traders in turn.",
        "Escalating permanent stats; cash out at any step.",
        TradeUp,
        Some(95_000),
        3,
        Route
    ),
];

/// Every workbook identity, rule and player-facing description participates in content negotiation.
pub(crate) fn visit_content(mut word: impl FnMut(u64)) {
    word(super::FIRST_BOARD_DEF as u64);
    word(QuestId::ALL.len() as u64);
    for quest in QuestId::ALL {
        let def = quest.definition();
        for text in [def.id, def.name, def.giver, def.objective, def.reward_text] {
            word(text.len() as u64);
            for byte in text.bytes() {
                word(u64::from(byte));
            }
        }
        word(u64::from(quest.home().code()));
        word(def.reward as u64);
        word(def.reward.kind() as u64);
        word(def.route_timer_ms.unwrap_or(0));
        word(u64::from(def.objective_count));
        word(def.clock as u64);
    }
    super::attempts::visit_rules(&mut word);
    super::rewards::visit_rules(&mut word);
    super::special::egg::visit_rules(&mut word);
    super::special::wall::visit_rules(&mut word);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn catalog_has_five_quests_per_zone_and_complete_definitions() {
        assert_eq!(QuestId::ALL.len(), 25);
        for faction in FactionId::ALL {
            assert_eq!(
                QuestId::ALL
                    .iter()
                    .filter(|id| id.home() == faction)
                    .count(),
                5
            );
        }
        for (index, id) in QuestId::ALL.into_iter().enumerate() {
            let d = id.definition();
            assert_eq!(d, &DEFINITIONS[index]);
            assert!(!d.id.is_empty() && !d.name.is_empty() && !d.giver.is_empty());
            assert!(!d.objective.is_empty() && !d.reward_text.is_empty());
            assert!(d.objective_count > 0);
            assert!(d.route_timer_ms.is_none_or(|ms| ms > 0));
        }
    }

    #[test]
    fn reward_kinds_match_the_board_totals() {
        let mut single = 0;
        let mut reusable = 0;
        let mut permanent = 0;
        for d in DEFINITIONS {
            match d.reward.kind() {
                RewardKind::SingleUse => single += 1,
                RewardKind::Reusable => reusable += 1,
                RewardKind::Permanent => permanent += 1,
            }
        }
        assert_eq!((single, reusable, permanent), (8, 10, 7));
    }
}
