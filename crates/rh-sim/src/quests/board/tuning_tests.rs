//! P-BAL-Q25-SEAM-1 oracle: guards on the Top 25 tuning table. One range per field with its reason, the couplings the
//! restore validators assume, and every number a quest text states matched to the field it restates.
use super::TOP25;
use crate::quests::board::catalog::QuestId::{self, *};

fn within(field: &str, value: f64, lo: f64, hi: f64, reason: &str) {
    assert!(
        (lo..=hi).contains(&value),
        "TOP25.{field} = {value} is outside {lo}..={hi}: {reason}"
    );
}

/// The field types of the table (and the bounds' literal types), widened to f64 for one comparison.
trait Wide {
    fn wide(self) -> f64;
}
impl Wide for f64 {
    fn wide(self) -> f64 {
        self
    }
}
impl Wide for i32 {
    fn wide(self) -> f64 {
        f64::from(self)
    }
}
impl Wide for u8 {
    fn wide(self) -> f64 {
        f64::from(self)
    }
}
impl Wide for u64 {
    fn wide(self) -> f64 {
        self as f64
    }
}
impl Wide for usize {
    fn wide(self) -> f64 {
        self as f64
    }
}

macro_rules! range {
    ($field:ident, $lo:expr, $hi:expr, $reason:literal) => {
        within(
            stringify!($field),
            TOP25.$field.wide(),
            $lo.wide(),
            $hi.wide(),
            $reason,
        )
    };
}

#[test]
fn every_field_sits_in_its_range() {
    let t = &TOP25;
    // Objectives and attempts.
    range!(
        fort_ms,
        30_000,
        60_000,
        "fort dwell 30-60 s: long enough to be contested, short enough to finish"
    );
    range!(
        meditate_ms,
        15_000,
        45_000,
        "meditation 15-45 s: a passer-by can interrupt it, a patient player finishes"
    );
    range!(
        crown_burn_rate,
        0.002,
        0.03,
        "crown burn 0.2-3% of max hp a second: a cost over the carry, not a death"
    );
    range!(
        crown_power_cap,
        12,
        120,
        "crown power cap 12-120 (one a second): the carry pays, never more than a farm"
    );
    range!(
        contact_range,
        32,
        128,
        "contact range 1-4 tiles: at the giver, not across the room"
    );
    range!(
        fort_range,
        48,
        128,
        "guard-house hold radius 1.5-4 tiles: inside the house, not the yard"
    );
    range!(
        brush_reveal_range,
        64,
        224,
        "brush tokens show at 2-7 tiles: hidden until close, findable"
    );
    range!(
        giver_flee_ms,
        20_000,
        120_000,
        "fled givers return in 20-120 s: a prank, not a lockout"
    );
    range!(
        quest_respawn_ms,
        10_000,
        60_000,
        "quest target camp respawn 10-60 s: a retry, not a farm"
    );
    range!(
        warden_hp_multiplier,
        1.5,
        6,
        "the Warden is a huge-stat elite: 1.5-6x a normal camp mob"
    );
    range!(
        bargain_cost,
        5,
        60,
        "the Bargain takes 5-60 stacks: affordable mid-match"
    );
    range!(
        route_pace,
        1000,
        3000,
        "route deadline floor 1-3x the straight walk time"
    );
    range!(
        route_slack_ms,
        0,
        30_000,
        "route slack 0-30 s on top of the walk"
    );
    // Reusable spell cooldowns and charges.
    let cooldown = "reusable cooldown 5-120 s: more than once a match, never spammable";
    for (field, ms) in [
        ("hook_cooldown_ms", t.hook_cooldown_ms),
        ("shoulder_charge_cooldown_ms", t.shoulder_charge_cooldown_ms),
        ("tornado_cooldown_ms", t.tornado_cooldown_ms),
        ("turret_cooldown_ms", t.turret_cooldown_ms),
        ("smite_cooldown_ms", t.smite_cooldown_ms),
        ("spell_shield_cooldown_ms", t.spell_shield_cooldown_ms),
        ("lantern_ward_cooldown_ms", t.lantern_ward_cooldown_ms),
        ("cleanse_cooldown_ms", t.cleanse_cooldown_ms),
        ("scout_falcon_cooldown_ms", t.scout_falcon_cooldown_ms),
        ("bolas_cooldown_ms", t.bolas_cooldown_ms),
    ] {
        within(field, ms, 5_000., 120_000., cooldown);
    }
    range!(
        lantern_ward_charges,
        1,
        4,
        "Lantern Ward holds 1-4 charges: 'reusable charges', a handful"
    );
    // Permanent rewards.
    for (i, stacks) in t.trade_up_stacks.into_iter().enumerate() {
        within(
            &format!("trade_up_stacks[{i}]"),
            stacks as f64,
            4.,
            120.,
            "a Trade Up step pays 4-120 stacks: worth a trade, never more than a full farm",
        );
    }
    range!(bargain_stacks, 5, 72, "the Bargain pays 5-72 stacks");
    range!(feed_damage_stacks, 2, 15, "Feed or Hunt pays +2-15% damage");
    range!(
        chef_bonus_share,
        0.05,
        0.5,
        "Chef's Kiss raises the lowest stat by 5-50%"
    );
    range!(chef_bonus_cap, 0.10, 0.5, "Chef's Kiss has a bounded gain");
    range!(
        chef_move_share,
        0.05,
        0.3,
        "Chef's Kiss has a separate movement scale"
    );
    range!(
        chef_move_cap,
        t.chef_move_share,
        t.chef_bonus_cap,
        "movement has a lower cap"
    );
    range!(
        helm_bypass,
        0.05,
        0.5,
        "Breaker's Helm lets 5-50% of axe and bow damage ignore armor"
    );
    // Spell effects.
    range!(
        stasis_ms,
        1000,
        5000,
        "Stasis 1-5 s: a dodge, not an escape from the match"
    );
    range!(shield_ms, 500, 5000, "Spell Shield window 0.5-5 s");
    range!(warhorn_ms, 1000, 5000, "Warhorn taunt and buff 1-5 s");
    range!(
        tornado_windup_ms,
        200,
        1500,
        "Tornado windup 0.2-1.5 s: readable, not dodge-proof"
    );
    range!(tornado_radius, 120, 480, "Tornado reach 4-15 tiles");
    range!(
        pull_tick_distance,
        40,
        160,
        "pull speed 40-160 units per legacy tick"
    );
    range!(
        smite_range,
        200,
        800,
        "Smite range 200-800 units: a burst at mid range"
    );
    range!(swap_range, 200, 800, "Swap-Stick range 200-800 units");
    range!(hook_range, 400, 1000, "Hook range 400-1000 units");
    range!(hook_width, 6, 32, "Hook shot half-width 6-32 units");
    range!(bolas_range, 300, 900, "Bolas range 300-900 units");
    range!(bolas_width, 6, 32, "Bolas shot half-width 6-32 units");
    range!(
        blind_rage_attack_rate,
        1.05,
        1.6,
        "BlindRage attacks 5-60% faster"
    );
    range!(
        warhorn_damage_per_taunt,
        0.05,
        0.4,
        "Warhorn +5-40% damage per enemy taunted"
    );
    range!(
        warhorn_guard_per_taunt,
        0.05,
        0.3,
        "Warhorn guard 5-30% less damage per enemy taunted"
    );
    range!(
        warhorn_guard_floor,
        0.2,
        0.8,
        "Warhorn guard never below 20-80% of the damage taken"
    );
    range!(
        tornado_cast_range,
        250,
        900,
        "Tornado placement range 250-900 units"
    );
    range!(
        warhorn_radius,
        150,
        500,
        "Warhorn taunt radius 150-500 units"
    );
    range!(
        smite_damage,
        100,
        500,
        "Smite bursts 100-500: steals kills, does not one-shot a full player"
    );
    range!(swap_ms, 3000, 20_000, "Swap-Stick lasts 3-20 s");
    range!(blind_rage_ms, 2000, 12_000, "BlindRage lasts 2-12 s");
    range!(still_mind_ms, 1000, 6000, "StillMind lasts 1-6 s");
    range!(charge_range, 120, 400, "Shoulder Charge dash 120-400 units");
    range!(
        charge_width,
        10,
        40,
        "Shoulder Charge hit half-width 10-40 units"
    );
    range!(
        charge_knockback,
        40,
        240,
        "Shoulder Charge knockback 40-240 units"
    );
    range!(charge_stun_ms, 200, 1500, "Shoulder Charge stun 0.2-1.5 s");
    range!(
        shot_speed,
        80,
        350,
        "Hook and Bolas fly 80-350 units per legacy tick"
    );
    range!(
        hook_stop_distance,
        20,
        96,
        "Hook pulls to 20-96 units of the caster: next to them, not inside"
    );
    range!(hook_pull_ms, 200, 1500, "Hook pull lasts 0.2-1.5 s");
    range!(bolas_root_ms, 500, 3000, "Bolas roots 0.5-3 s");
    range!(
        tornado_pull,
        60,
        360,
        "Tornado pulls up to 60-360 units at full strength"
    );
    range!(tornado_pull_ms, 100, 1000, "Tornado pull lasts 0.1-1 s");
    range!(
        taunt_stop_distance,
        20,
        96,
        "a taunted player walks to 20-96 units of the source"
    );
    // Cage and deployments.
    range!(cage_radius, 96, 256, "Warden's Cage ring radius 3-8 tiles");
    range!(cage_ms, 3000, 10_000, "Warden's Cage lasts 3-10 s");
    range!(
        falcon_speed_per_ms,
        1,
        12,
        "Scout Falcon flies 1-12 units per ms"
    );
    range!(
        cage_gap_half_angle,
        0.2,
        1.0,
        "the cage's open gap is 0.2-1.0 rad either side of the facing"
    );
    range!(turret_ms, 10_000, 60_000, "a turret stands 10-60 s");
    range!(
        turret_interval_ms,
        400,
        2000,
        "a turret shoots every 0.4-2 s"
    );
    range!(turret_range, 200, 600, "turret reach 200-600 units");
    range!(
        turret_reward_range,
        200,
        800,
        "a turret kill farther than 200-800 units is remote"
    );
    range!(ward_ms, 30_000, 150_000, "a Lantern Ward lasts 30-150 s");
    range!(falcon_ms, 1500, 8000, "a Scout Falcon reveal lasts 1.5-8 s");
    range!(
        cage_cast_range,
        200,
        700,
        "the cage is cast 200-700 units away"
    );
    range!(
        deploy_range,
        200,
        700,
        "turrets and wards are placed 200-700 units away"
    );
    range!(
        turret_hp,
        40,
        300,
        "a turret has 40-300 hp: breakable, not a wall"
    );
    range!(ward_hp, 10, 120, "a ward has 10-120 hp");
    range!(turret_cap, 1, 2, "1-2 turrets per owner");
    range!(ward_cap, 1, 4, "1-4 wards per owner");
    range!(
        falcon_cap,
        1,
        1,
        "a restore bound only: the cast path drops the owner's previous falcon (effects/terrain.rs:266), so play makes one"
    );
    range!(
        falcon_range,
        600,
        2000,
        "the falcon flies 600-2000 units: 'a long reveal shot'"
    );
    range!(turret_reveal, 100, 400, "a turret reveals 100-400 units");
    range!(ward_reveal, 200, 600, "a ward reveals 200-600 units");
    range!(
        falcon_reveal,
        80,
        300,
        "the falcon reveals 80-300 units around its path"
    );
    range!(
        turret_damage_share,
        0.1,
        0.8,
        "a turret hits for 10-80% of its owner's weapon rate"
    );
    // Rebirth Egg.
    range!(
        egg_safe_distance,
        300,
        1000,
        "an egg needs 300-1000 units from enemies to hatch"
    );
    range!(
        egg_sight_radius,
        100,
        400,
        "an egg is seen from 100-400 units"
    );
    range!(egg_hatch_ms, 1000, 6000, "an egg hatches in 1-6 s");
    range!(egg_hatch_hp_share, 0.2, 0.8, "a hatch returns 20-80% hp");
    range!(egg_min_standing, 2, 8, "eggs work while 2-8 players stand");
    // WallBuster.
    range!(
        wall_radius,
        150,
        500,
        "WallBuster opens walls within 150-500 units"
    );
    range!(
        wall_anchor_margin,
        0,
        64,
        "WallBuster anchor margin 0-2 tiles"
    );
}

#[test]
fn couplings_the_restore_validators_assume() {
    let t = &TOP25;
    assert!(
        t.meditate_ms <= t.fort_ms,
        "attempts.rs validation caps every dwell (fort and meditation) at fort_ms"
    );
    assert!(
        t.hook_pull_ms <= t.warhorn_ms && t.tornado_pull_ms <= t.warhorn_ms,
        "effects/checkpoint.rs bounds every control row (pulls and taunts) by warhorn_ms"
    );
    assert!(
        t.trade_up_stacks.windows(2).all(|w| w[0] < w[1]),
        "Trade Up is 'escalating': each step pays more than the last"
    );
    assert!(
        t.bargain_stacks >= t.bargain_cost,
        "the Bargain converts at a premium: it pays at least what it takes"
    );
}

fn seconds(ms: f64) -> String {
    format!("{}", ms / 1000.)
}

fn word_seconds(ms: f64) -> String {
    // A local, not a const: tests/content.rs scans every const in src/ (this file included).
    let words = [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve",
    ];
    let s = ms / 1000.;
    assert!(s.fract() == 0. && s <= 12., "{ms} ms has no number word");
    words[s as usize].to_string()
}

fn percent(share: f64) -> String {
    format!("{}", (share * 100.).round())
}

/// Every number a quest text states, paired with the field it restates: (quest, which text, the phrase built from
/// TOP25). A retune that moves the number and not the text, or the text and not the number, fails here.
#[test]
fn reward_text_numbers_match_tuning() {
    let t = &TOP25;
    let pairs: Vec<(QuestId, &str, String)> = vec![
        (
            TheWarden,
            "reward",
            format!("a {}-second fence ring", word_seconds(t.cage_ms)),
        ),
        (
            HoldTheFort,
            "objective",
            format!("for {} seconds", seconds(t.fort_ms)),
        ),
        (
            LetHimCook,
            "reward",
            format!(
                "+{}% to your least-grown stat, capped at {}% of its starting value. If it is move speed, gain +{}% capped at {}% of starting speed",
                percent(t.chef_bonus_share), percent(t.chef_bonus_cap), percent(t.chef_move_share), percent(t.chef_move_cap),
            ),
        ),
        (
            TuberEats,
            "reward",
            format!(
                "{}% of axe and bow damage ignores armor",
                percent(t.helm_bypass)
            ),
        ),
        (
            BarrowRun,
            "reward",
            format!("within {} units", t.wall_radius),
        ),
        (
            TheBargain,
            "reward",
            format!(
                "Convert {} stacks into {} of a stat",
                t.bargain_cost, t.bargain_stacks
            ),
        ),
        (
            ACruelJoke,
            "objective",
            format!("flee for {} seconds", seconds(t.giver_flee_ms)),
        ),
        (
            ACruelJoke,
            "reward",
            format!("with a target for {} seconds", seconds(t.swap_ms)),
        ),
        (
            ABoneToPick,
            "reward",
            format!("roots for {} seconds", seconds(t.bolas_root_ms)),
        ),
        (
            FeedOrHunt,
            "reward",
            format!("+{}% damage", t.feed_damage_stacks),
        ),
        (
            Gatecrasher,
            "reward",
            format!("with {}% HP", percent(t.egg_hatch_hp_share)),
        ),
        (
            StayAWhile,
            "objective",
            format!(
                "a {}-second uninterrupted meditation",
                seconds(t.meditate_ms)
            ),
        ),
        (
            StayAWhile,
            "reward",
            format!("Stasis: {} seconds invulnerable", seconds(t.stasis_ms)),
        ),
    ];
    for (quest, which, phrase) in pairs {
        let d = quest.definition();
        let text = if which == "reward" {
            d.reward_text
        } else {
            d.objective
        };
        assert!(
            text.contains(&phrase),
            "{quest:?} {which} text {text:?} does not say {phrase:?}: the text and TOP25 disagree"
        );
    }
}
