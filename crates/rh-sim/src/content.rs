//! Content tables of the slice: the faction sheet, the weapon catalog and the damage law, copied from the legacy
//! `packages/shared/src/factions.ts` and `weapons.ts` at the tested revision. Content is versioned by [`content_hash`]: a
//! server and a client (or a replay) with different hashes are running different rules and must not be mixed.
//!
//! ## One registry, one rule
//!
//! Every gameplay number is declared once, as one line of the `content!` registry in [`crate::constants`] (name, type,
//! value, unit). That line is all a new tunable needs: it becomes the `pub const` the code uses and a row of
//! [`REGISTRY`], and [`content_hash`] folds the whole registry, so the rules identity cannot miss it. Rules that are code
//! rather than numbers are registered as data here (the faction and weapon tables, [`STAGGER_WEAPONS`], the player
//! windup formula's constants) and are folded too; the derived-value probes in [`content_hash`] additionally pin the
//! formulas (`attack_interval`, `damage_per_hit`, `pvp_factor`, `bounty_for`, `player_timings`) to their results.
//! `tests/content.rs` fails when a `const` appears anywhere in `crates/rh-sim/src` that is neither registered nor listed
//! (with a reason) as not gameplay, and when changing any single registry row leaves the hash unchanged.
//!
//! ## Durable ids (append only)
//!
//! The `as_str` spellings of [`FactionId`] and [`WeaponId`], and the cosmetic and achievement id strings the profile store
//! keeps, are DURABLE identifiers: they are jsonb keys and values in stored profiles (`winsByFaction`, `cosmetics`,
//! `achievements`) and in match records. Add new ones, never rename or reuse one. `crates/rh-server/tests/durable_ids.rs`
//! pins the strings and fails on any change.

use crate::constants::*;
pub use crate::constants::{ContentVal, Entry, REGISTRY};
use crate::math::{exp, PI};

#[derive(
    Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, serde::Serialize, serde::Deserialize,
)]
pub enum FactionId {
    Fire,
    Sentenced,
    Plague,
    Frost,
    Holy,
}

impl FactionId {
    pub const ALL: [FactionId; 5] = [
        FactionId::Fire,
        FactionId::Sentenced,
        FactionId::Plague,
        FactionId::Frost,
        FactionId::Holy,
    ];
    pub fn as_str(self) -> &'static str {
        match self {
            FactionId::Fire => "fire",
            FactionId::Sentenced => "sentenced",
            FactionId::Plague => "plague",
            FactionId::Frost => "frost",
            FactionId::Holy => "holy",
        }
    }
    pub fn parse(s: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|f| f.as_str() == s)
    }
    /// Wire code (index in [`FactionId::ALL`]; append only).
    pub fn code(self) -> u8 {
        self as u8
    }
    pub fn from_code(c: u8) -> Option<Self> {
        Self::ALL.get(c as usize).copied()
    }
    /// Legacy `defaultFaction`: round robin over join order.
    pub fn default_for(join_index: usize) -> Self {
        Self::ALL[join_index % Self::ALL.len()]
    }
}

/// The sheet numbers the slice reads (signature params are out of the slice: docs/backlog).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct FactionSpec {
    pub hp: f64,
    pub rate: f64,
    pub hit: f64,
    pub phys: f64,
    pub true_share: f64,
    pub armor: f64,
    pub shield: f64,
}

pub fn faction(id: FactionId) -> FactionSpec {
    match id {
        FactionId::Fire => FactionSpec {
            hp: 950.0,
            rate: 0.6,
            hit: 85.2,
            phys: 0.0,
            true_share: 0.0,
            armor: 20.0,
            shield: 20.0,
        },
        FactionId::Sentenced => FactionSpec {
            hp: 1085.0,
            rate: 0.95,
            hit: 72.1,
            phys: 0.4,
            true_share: 0.0,
            armor: 94.0,
            shield: 94.0,
        },
        FactionId::Plague => FactionSpec {
            hp: 1020.0,
            rate: 1.85,
            hit: 28.7,
            phys: 0.8,
            true_share: 0.0,
            armor: 77.0,
            shield: 77.0,
        },
        FactionId::Frost => FactionSpec {
            hp: 985.0,
            rate: 0.45,
            hit: 168.2,
            phys: 0.9,
            true_share: 0.0,
            armor: 116.0,
            shield: 116.0,
        },
        FactionId::Holy => FactionSpec {
            hp: 1145.0,
            rate: 0.95,
            hit: 92.2,
            phys: 1.0,
            true_share: 0.0,
            armor: 78.0,
            shield: 78.0,
        },
    }
}

/// The faction as fought (legacy `pacedFaction`): the hit times DUEL_PACE, hp unchanged.
pub fn paced(id: FactionId) -> FactionSpec {
    let f = faction(id);
    FactionSpec {
        hit: f.hit * DUEL_PACE,
        ..f
    }
}

#[derive(Clone, Copy, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct DamageShares {
    pub phys: f64,
    pub magic: f64,
    pub true_: f64,
}

pub fn magic_share(f: &FactionSpec) -> f64 {
    (1.0 - f.phys - f.true_share).max(0.0)
}

pub fn damage_shares(f: &FactionSpec) -> DamageShares {
    DamageShares {
        phys: f.phys,
        magic: magic_share(f),
        true_: f.true_share,
    }
}

/// The damage law for a hit split into shares (legacy `mitigateShares`).
pub fn mitigate_shares(raw: f64, s: &DamageShares, armor: f64, shield: f64) -> f64 {
    raw * (s.phys / (1.0 + armor / 100.0) + s.magic / (1.0 + shield / 100.0) + s.true_)
}

/// PvP outgoing multiplier for card power `p` (legacy `pvpFactor`).
pub fn pvp_factor(power: f64) -> f64 {
    let p = if power > 0.0 { power } else { 0.0 };
    PVP_F0 + (PVP_FMAX - PVP_F0) * (1.0 - exp(-p / PVP_P0))
}

/// Legacy `bountyFor`: GV a player kill pays on top once the victim has more than BOUNTY_FREE_KILLS player kills.
pub fn bounty_for(player_kills: i64) -> u32 {
    let over = player_kills - BOUNTY_FREE_KILLS;
    if over > 0 {
        crate::math::js_round(BOUNTY_SCALE * crate::math::pow(over as f64, BOUNTY_EXPONENT)) as u32
    } else {
        0
    }
}

#[derive(
    Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, serde::Serialize, serde::Deserialize,
)]
pub enum WeaponId {
    Sword,
    Bow,
    Wand,
    Shield,
    Axe,
    Fists,
}

/// Source `WeaponSpec.onHitPower`: how hard a weapon's on-hit effects land.
pub const fn on_hit_power(weapon: WeaponId) -> f64 {
    match weapon {
        WeaponId::Sword => crate::constants::SWORD_ON_HIT_POWER,
        WeaponId::Fists => crate::constants::FISTS_ON_HIT_POWER,
        _ => crate::constants::PLAIN_ON_HIT_POWER,
    }
}

impl WeaponId {
    /// Wire order of `ALL_WEAPON_IDS` (legacy): the five loop weapons, then fists. Append only.
    pub const ALL: [WeaponId; 6] = [
        WeaponId::Sword,
        WeaponId::Bow,
        WeaponId::Wand,
        WeaponId::Shield,
        WeaponId::Axe,
        WeaponId::Fists,
    ];
    pub fn as_str(self) -> &'static str {
        match self {
            WeaponId::Sword => "sword",
            WeaponId::Bow => "bow",
            WeaponId::Wand => "wand",
            WeaponId::Shield => "shield",
            WeaponId::Axe => "axe",
            WeaponId::Fists => "fists",
        }
    }
    pub fn parse(s: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|w| w.as_str() == s)
    }
    pub fn code(self) -> u8 {
        self as u8
    }
    pub fn from_code(c: u8) -> Option<Self> {
        Self::ALL.get(c as usize).copied()
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Shape {
    Arc { reach: f64, half_arc: f64 },
    Projectile { speed: f64, range: f64 },
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WeaponSpec {
    pub boost: f64,
    pub floor: f64,
    pub dps_mult: f64,
    pub shape: Shape,
}

pub fn weapon(id: WeaponId) -> WeaponSpec {
    match id {
        WeaponId::Sword => WeaponSpec {
            boost: 1.15,
            floor: 0.25,
            dps_mult: 0.75,
            shape: Shape::Arc {
                reach: 112.0,
                half_arc: PI / 4.0,
            },
        },
        WeaponId::Bow => WeaponSpec {
            boost: 0.95,
            floor: 0.25,
            dps_mult: 0.8,
            shape: Shape::Projectile {
                speed: 900.0,
                range: 520.0,
            },
        },
        WeaponId::Wand => WeaponSpec {
            boost: 0.65,
            floor: 0.3,
            dps_mult: 0.8,
            shape: Shape::Projectile {
                speed: 380.0,
                range: 640.0,
            },
        },
        WeaponId::Shield => WeaponSpec {
            boost: 0.6,
            floor: 0.3,
            dps_mult: 0.75,
            shape: Shape::Arc {
                reach: 96.0,
                half_arc: PI / 3.0,
            },
        },
        WeaponId::Axe => WeaponSpec {
            boost: 0.4,
            floor: 0.35,
            dps_mult: 0.77,
            shape: Shape::Arc {
                reach: 112.0,
                half_arc: PI / 2.0,
            },
        },
        WeaponId::Fists => WeaponSpec {
            boost: 1.5,
            floor: 0.2,
            dps_mult: 0.6,
            shape: Shape::Arc {
                reach: 72.0,
                half_arc: PI / 3.0,
            },
        },
    }
}

/// Seconds between attacks (legacy `attackInterval`).
pub fn attack_interval(f: &FactionSpec, w: WeaponId, as_mult: f64) -> f64 {
    let s = weapon(w);
    s.floor.max(1.0 / (f.rate * (1.0 + s.boost) * as_mult))
}

/// Raw damage of one hit before mitigation (legacy `damagePerHit`).
pub fn damage_per_hit(f: &FactionSpec, w: WeaponId, as_mult: f64) -> f64 {
    f.rate * f.hit * weapon(w).dps_mult * as_mult * attack_interval(f, w, as_mult)
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Timings {
    pub interval: f64,
    pub windup: f64,
    pub root: f64,
    pub ranged: bool,
}

/// Player timings (legacy `weaponTimings(w, interval, "player")`): player melee has no windup; bow and wand keep theirs.
/// The NPC profile (mob telegraphs) is not in the slice.
pub fn player_timings(w: WeaponId, interval: f64) -> Timings {
    let ranged = matches!(weapon(w).shape, Shape::Projectile { .. });
    let mut t = Timings {
        interval,
        windup: 0.0,
        root: 0.0,
        ranged,
    };
    match w {
        WeaponId::Bow => t.windup = BOW_WINDUP_MAX_S.min(BOW_WINDUP_INTERVAL_SHARE * interval),
        WeaponId::Wand => t.windup = WAND_WINDUP_MAX_S.min(WAND_WINDUP_INTERVAL_SHARE * interval),
        _ => {}
    }
    t
}

/// Source weaponTimings NPC profile; all attack arithmetic still uses this shared weapon catalog.
pub fn npc_timings(w: WeaponId, interval: f64) -> Timings {
    let mut t = player_timings(w, interval);
    match w {
        WeaponId::Sword => t.windup = NPC_SWORD_WINDUP.min(interval),
        WeaponId::Shield => t.windup = NPC_BASH_WINDUP.min(interval),
        WeaponId::Fists => t.windup = NPC_FISTS_WINDUP.min(interval),
        WeaponId::Axe => {
            t.windup = NPC_AXE_WINDUP.min(NPC_AXE_WINDUP_SHARE * interval);
            t.root = NPC_AXE_ROOT.min(NPC_AXE_ROOT_SHARE * interval);
        }
        WeaponId::Bow => t.windup = NPC_BOW_WINDUP.min(NPC_BOW_WINDUP_SHARE * interval),
        WeaponId::Wand => t.windup = NPC_WAND_WINDUP.min(NPC_WAND_WINDUP_SHARE * interval),
    }
    t
}

/// Source shared/mods.ts scaleWindup, with ties/operation order unchanged.
pub fn scale_windup(t: Timings, windup_mult: f64) -> Timings {
    if windup_mult == 1. || !windup_mult.is_finite() || (t.ranged && windup_mult < 1.) {
        return t;
    }
    let windup = if t.ranged {
        t.windup.max(t.interval.min(t.windup * windup_mult))
    } else {
        t.interval.min(t.windup * windup_mult)
    };
    let root = if t.root > 0. {
        windup.max(t.interval.min(t.root * windup_mult))
    } else {
        0.
    };
    Timings { windup, root, ..t }
}

/// The actual source player/NPC mirrored timings. Zombies retain their separate explicit timing profile.
pub fn mirror_timings(
    f: &FactionSpec,
    w: WeaponId,
    as_mult: f64,
    windup_mult: f64,
    npc: bool,
) -> Timings {
    let timing = if npc { npc_timings } else { player_timings };
    let mut t = timing(w, attack_interval(f, w, as_mult));
    if npc && t.ranged && as_mult > 1. {
        let base = timing(w, attack_interval(f, w, 1.)).windup;
        if t.windup < base {
            t.windup = base;
        }
    }
    scale_windup(t, windup_mult)
}

/// Weapons whose hits stagger the victim (legacy: the sword). A rule flag registered as data so the rules identity covers
/// it; the sim's stagger check still compares against `WeaponId::Sword` directly until it is switched to read this table.
pub const STAGGER_WEAPONS: &[WeaponId] = &[WeaponId::Sword];

/// Whether a hit by `w` staggers (reads [`STAGGER_WEAPONS`]).
pub fn staggers(w: WeaponId) -> bool {
    STAGGER_WEAPONS.contains(&w)
}

/// FNV-1a over a byte stream; the only hasher of the rules identity.
struct Fnv(u64);

impl Fnv {
    fn new() -> Self {
        Fnv(0xcbf2_9ce4_8422_2325)
    }
    fn byte(&mut self, b: u8) {
        self.0 ^= b as u64;
        self.0 = self.0.wrapping_mul(0x0000_0100_0000_01b3);
    }
    fn u64(&mut self, v: u64) {
        for b in v.to_le_bytes() {
            self.byte(b);
        }
    }
    fn f64(&mut self, v: f64) {
        self.u64(v.to_bits());
    }
    /// Length-prefixed so that adjacent strings cannot run together.
    fn str(&mut self, s: &str) {
        self.u64(s.len() as u64);
        for b in s.bytes() {
            self.byte(b);
        }
    }
    fn val(&mut self, v: &ContentVal) {
        match *v {
            ContentVal::F64(x) => {
                self.byte(1);
                self.f64(x);
            }
            ContentVal::U64(x) => {
                self.byte(2);
                self.u64(x);
            }
            ContentVal::I64(x) => {
                self.byte(3);
                self.u64(x as u64);
            }
        }
    }
}

mod definitions;
mod fold;
mod hash;

pub use fold::*;
pub use hash::*;

use definitions::content_hash_of_definitions;
use fold::{fold_mob_catalog, fold_status_content, fold_status_stream, RuntimeWords};

/// Borrowed rule definitions avoid duplicating default lists at every perturbation entry point.
struct MapDefinitions<'rules, 'pillars> {
    coliseum: &'rules crate::mapgen::coliseum::FixedDefinition<'pillars>,
    zones: &'rules crate::mapgen::zones::ZoneDefinition,
    camps: &'rules crate::mapgen::zones::camps::CampDefinition,
    attempt: &'rules crate::mapgen::attempt::AttemptDefinition,
    spawns: &'rules crate::mapgen::spawns::SpawnDefinition,
    bushes: &'rules crate::mapgen::bushes::BushDefinition,
    landmarks: &'rules crate::mapgen::landmarks::runtime::LandmarkDefinition,
    landmark_search: &'rules crate::mapgen::landmarks::search::SearchDefinition,
    recipe_support: &'rules crate::mapgen::landmarks::recipe_support::RecipeSupportDefinition,
    recipes: crate::mapgen::landmarks::recipes::Definitions<'pillars>,
    records: &'rules crate::mapgen::records::Definition<'pillars>,
    terrain_gate: &'rules crate::mapgen::terrain_gate::TerrainGateDefinition<'pillars>,
    style_features: &'rules crate::mapgen::style_features::Definition,
    style_sanctum: &'rules crate::mapgen::style_sanctum::Definition,
    style_warren: crate::mapgen::style_warren::Definition<'pillars>,
    style_scorch: crate::mapgen::style_scorch::Definition,
    style_rime: crate::mapgen::style_rime::Definition,
    style_gallows: crate::mapgen::style_gallows::Definition<'pillars>,
    style_gallows_dials: crate::mapgen::style_gallows::Dials,
    style_registry: &'rules [crate::mapgen::style_zones::Entry],
    pipeline: &'rules crate::mapgen::pipeline::Definition,
    certify: &'rules crate::mapgen::certify::rules::Definition,
    style_runtime: &'rules crate::mapgen::style_runtime::Definition,
    spots: &'rules crate::mapgen::spots::Definition,
    tail: &'rules crate::mapgen::tail::Definition,
    repair: crate::mapgen::repair::Definition<'pillars>,
}

impl Default for MapDefinitions<'_, '_> {
    fn default() -> Self {
        Self {
            coliseum: &crate::mapgen::coliseum::DEFAULT_DEFINITION,
            zones: &crate::mapgen::zones::DEFAULT_DEFINITION,
            camps: &crate::mapgen::zones::camps::DEFAULT_DEFINITION,
            attempt: &crate::mapgen::attempt::DEFAULT_DEFINITION,
            spawns: &crate::mapgen::spawns::DEFAULT_DEFINITION,
            bushes: &crate::mapgen::bushes::DEFAULT_DEFINITION,
            landmarks: &crate::mapgen::landmarks::runtime::DEFAULT_DEFINITION,
            landmark_search: &crate::mapgen::landmarks::search::DEFAULT_DEFINITION,
            recipe_support: &crate::mapgen::landmarks::recipe_support::DEFAULT_DEFINITION,
            recipes: crate::mapgen::landmarks::recipes::default_definitions(),
            records: &crate::mapgen::records::DEFAULT_DEFINITION,
            terrain_gate: &crate::mapgen::terrain_gate::DEFAULT_DEFINITION,
            style_features: &crate::mapgen::style_features::DEFAULT_DEFINITION,
            style_sanctum: &crate::mapgen::style_sanctum::DEFAULT_DEFINITION,
            style_warren: crate::mapgen::style_warren::default_definition(),
            style_scorch: crate::mapgen::style_scorch::default_definition(),
            style_rime: crate::mapgen::style_rime::default_definition(),
            style_gallows: crate::mapgen::style_gallows::default_definition(),
            style_gallows_dials: crate::mapgen::style_gallows::default_dials(),
            style_registry: &crate::mapgen::style_zones::ENTRIES,
            pipeline: &crate::mapgen::pipeline::DEFAULT_DEFINITION,
            certify: &crate::mapgen::certify::rules::R,
            style_runtime: &crate::mapgen::style_runtime::DEFAULT_DEFINITION,
            spots: &crate::mapgen::spots::DEFAULT_DEFINITION,
            tail: &crate::mapgen::tail::DEFAULT_DEFINITION,
            repair: crate::mapgen::repair::default_definition(
                &crate::mapgen::terrain_gate::GENERATOR_TILE_CLASSES,
            ),
        }
    }
}

/// Holy's unmodified sustain share. Cards and camp rewards are separate future producers.
pub fn holy_lifesteal_fraction(hp_frac: f64) -> f64 {
    HOLY_LEECH_CAP.min(HOLY_LEECH_BASE + HOLY_LEECH_MISSING * (1.0 - hp_frac))
}

/// Source-exact base-player Mark pulse before mitigation. Tier/linger amplifiers and build/hunt
/// modifiers are neutral in this installation. The counter's kill cap is enforced where it grows;
/// the source percentage helper itself does not clamp a supplied sentence count.
pub fn sentenced_pulse(weapon_id: WeaponId, target_max_hp: f64, sentence: u8, near: bool) -> f64 {
    let spec = paced(FactionId::Sentenced);
    let weight = spec.rate * weapon(weapon_id).dps_mult * attack_interval(&spec, weapon_id, 1.0);
    let pct = MARK_BASE_PCT * DUEL_PACE + (MARK_KILL_PCT * DUEL_PACE) * f64::from(sentence);
    pct * target_max_hp * weight * if near { 1.0 } else { MARK_FAR_SHARE }
}
