//! P-TICK-2 (ADR 0029): a bot reads the sim's step. Its context says the sim's `tick_ms`, and a seat keeps the wall-clock
//! meaning of its cadence: a policy that decides every 2 legacy ticks (100 ms) decides every 4 steps on a 25 ms sim.
use rh_policy::scripted::{Brawler, Idle};
use rh_policy::{drive_seats, MatchStart, Seat};
use rh_sim::{FactionId, Placement, PlayerId, Secrets, Sim, SimConfig, Vec2};

fn sim(tick_ms: u32) -> Sim {
    let place = Placement {
        profile: "a".into(),
        faction: FactionId::ALL[0],
        bot: true,
        at: Vec2::new(2000.0, 2000.0),
        hp: None,
        weapon0: None,
        weapon1: None,
    };
    Sim::reset_active(
        SimConfig {
            tick_ms,
            ..SimConfig::default()
        },
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(1),
        &[place],
    )
}

#[test]
fn a_match_start_carries_the_sims_step() {
    for ms in [50u32, 25] {
        let sim = sim(ms);
        assert_eq!(sim.tick_ms(), ms);
        let m = MatchStart::of(&sim, PlayerId(1), 1);
        assert_eq!(m.tick_ms, f64::from(ms), "tick_ms of the {ms} ms sim");
    }
}

#[test]
fn a_seat_decides_per_second_at_the_same_rate_at_both_steps() {
    // Brawler::new(2): every 2 legacy ticks = 100 ms = 10 decisions per second, whatever the sim's step.
    let seconds = 4u32;
    let mut per_second = Vec::new();
    for ms in [50u32, 25] {
        let mut sim = sim(ms);
        let me = sim.players()[0].id;
        let mut seats = vec![Seat::new(
            &MatchStart::of(&sim, me, 3),
            Box::new(Brawler::new(2)),
            Box::new(Idle),
        )];
        seats[0].bind_fx(&sim).unwrap();
        for _ in 0..(seconds * 1000 / ms) {
            drive_seats(&mut sim, &mut seats, &[]);
            sim.step();
        }
        assert!(seats[0].fault.is_none());
        per_second.push(seats[0].stats.decisions as f64 / f64::from(seconds));
    }
    assert!(
        per_second[0] >= 9.0,
        "the 50 ms seat decides about 10 per second, not {}",
        per_second[0]
    );
    assert!(
        (per_second[0] - per_second[1]).abs() <= 1.0,
        "decisions per second: {} at 50 ms, {} at 25 ms",
        per_second[0],
        per_second[1]
    );
}
