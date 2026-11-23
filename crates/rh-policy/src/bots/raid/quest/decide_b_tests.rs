//! P-RAID-Q3b unit tests: Two Banners' branch price, Trade Up's cash-out rule, the hold cap and the `Prices` fields.
use super::decide::{banner_branch, held_minutes, reach_chance, trade_banked, trade_go_on};
use crate::bots::raid::values::Prices;

const P: Prices = Prices::LOCAL;

#[test]
fn the_walk_back_never_makes_branch_one_win_and_a_tie_takes_branch_zero() {
    for death in [0.02, 0.2, 0.5] {
        assert_eq!(banner_branch(&P, 3., death, 0.4, 0.4), 0);
        assert_eq!(
            banner_branch(&P, 3., death, 0.4, 0.),
            0,
            "a tie is branch 0"
        );
    }
    // The rule follows minutes: a branch that cost fewer would win.
    assert_eq!(banner_branch(&P, 3., 0.02, 0.4, -0.2), 1);
}

#[test]
fn trade_up_goes_on_while_the_next_stage_pays_and_cashes_out_when_it_does_not() {
    // Stage two after one barter: extra 18/54 of 5.6 = 1.87.
    assert!(trade_go_on(&P, 1, 60., 0.2, 0.02));
    // The death term outweighs the extra worth.
    assert!(!trade_go_on(&P, 1, 60., 0.2, 0.5));
    // The timer cannot be beaten to the next trader: cash out.
    assert!(!trade_go_on(&P, 1, 5., 0.5, 0.02));
    assert!(!trade_go_on(&P, 2, 10., 0.5, 0.02));
    // The extra minutes cost what roaming earns.
    assert!(!trade_go_on(&P, 1, 100_000., 1_000., 0.02));
    // Three barters: nothing to go on to.
    assert!(!trade_go_on(&P, 3, 60., 0.2, 0.02));
}

#[test]
fn a_cash_out_never_holds_progress_into_an_expired_timer() {
    for progress in 1..3 {
        for left in [0., 1., 3.] {
            assert!(
                !trade_go_on(&P, progress, left, 0.3, 0.02),
                "{progress} {left}"
            );
        }
    }
    assert_eq!(reach_chance(f64::INFINITY, 5.), 1.);
    assert_eq!(reach_chance(0., 0.3), 0.);
    assert_eq!(reach_chance(600., 0.3), 1.);
}

#[test]
fn a_committed_quest_goal_is_priced_under_the_leave_rate() {
    let leave = P.sustain.leave_value / (P.sustain.leave_seconds / 60.);
    for net in [1., 3., 5.6] {
        let minutes = held_minutes(&P, net, 0.);
        assert!(net / minutes < leave * P.quest_hold_share + 1e-9);
        assert!(net / minutes < leave);
    }
    // A long walk is untouched, a table with sustain off is untouched.
    assert_eq!(held_minutes(&P, 3., 5.), 5.);
    let off = Prices::farm_only();
    assert_eq!(held_minutes(&off, 3., 0.), 0.);
}

#[test]
fn the_stage_shares_are_strictly_increasing_and_end_at_most_one() {
    let s = std::hint::black_box(P.trade_up_stage_share);
    assert!(0. < s[0] && s[0] < s[1] && s[1] < s[2] && s[2] <= 1.);
    assert!(trade_banked(&P, 0) == 0. && trade_banked(&P, 3) <= P.reward_value("trade_up"));
}

#[test]
fn the_hold_share_is_a_share_below_one() {
    let share = std::hint::black_box(P.quest_hold_share);
    assert!(0. < share && share < 1.);
}

/// The sim's `trade_up_stacks: [a, b, c],` line, parsed (loud on a format change), as `spells.rs` guards its ranges.
fn sim_stacks() -> [f64; 3] {
    const TUNING: &str = include_str!("../../../../../rh-sim/src/quests/board/tuning.rs");
    let line = TUNING
        .lines()
        .find_map(|l| l.trim().strip_prefix("trade_up_stacks: ["))
        .expect("tuning.rs has no `trade_up_stacks: [a, b, c],` line");
    let nums: Vec<f64> = line
        .trim_end_matches(',')
        .trim_end_matches(']')
        .split(',')
        .map(|n| n.trim().parse().expect("a stack count"))
        .collect();
    nums.try_into().expect("three stage sizes")
}

#[test]
fn the_stage_shares_equal_the_sims_stack_ratios() {
    let stacks = sim_stacks();
    for (share, stack) in P.trade_up_stage_share.iter().zip(stacks) {
        assert!(
            (share - stack / stacks[2]).abs() < 1e-9,
            "{share} vs {stack}"
        );
    }
}
