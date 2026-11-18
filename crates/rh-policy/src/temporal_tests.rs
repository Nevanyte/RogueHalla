use super::*;
use rh_sim::{FactionId, FactionMode, Placement, RawCommand, Secrets, SimConfig, Vec2};
fn scene() -> Sim {
    scene_with(false)
}
fn scene_with(fire: bool) -> Sim {
    Sim::reset_active(
        SimConfig {
            quests: false,
            faction_mode: if fire {
                FactionMode::EngagementHolyPlagueFire
            } else {
                FactionMode::EngagementHolyPlague
            },
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(1),
        &(0..3)
            .map(|i| Placement {
                profile: format!("p{i}"),
                faction: if i == 0 {
                    if fire {
                        FactionId::Fire
                    } else {
                        FactionId::Plague
                    }
                } else {
                    FactionId::Holy
                },
                bot: false,
                at: Vec2::new(400. + 56. * i as f64, 320.),
                hp: None,
                weapon0: None,
                weapon1: None,
            })
            .collect::<Vec<_>>(),
    )
}
#[test]
fn fire_instant_and_delayed_sync_async_projection() {
    let mut sim = scene_with(true);
    sim.submit(
        PlayerId(2),
        rh_sim::validate(RawCommand::Attack {
            a: std::f64::consts::PI,
            t: None,
        })
        .unwrap(),
    )
    .unwrap();
    let start = MatchStart::of(&sim, PlayerId(2), 1);
    let mut seats = vec![Seat::new(
        &start,
        Box::new(scripted::Idle),
        Box::new(scripted::Idle),
    )];
    let mut asynchronous = AsyncSeat::spawn(
        &start,
        Box::new(scripted::Idle),
        Box::new(scripted::Idle),
        2,
    );
    seats[0].bind_fx(&sim).unwrap();
    asynchronous.bind_fx(&sim).unwrap();
    sim.submit(
        PlayerId(1),
        rh_sim::validate(RawCommand::Attack { a: 0., t: None }).unwrap(),
    )
    .unwrap();
    let mut empty = 0;
    for _ in 0..20 {
        let events = sim.step();
        empty += usize::from(events.is_empty());
        feed_seats(&sim, &mut seats);
        asynchronous.feed_fx(&sim);
        let count = seats[0].pending.len();
        feed_seats(&sim, &mut seats);
        asynchronous.feed_fx(&sim);
        assert_eq!(seats[0].pending.len(), count);
    }
    assert_eq!(seats[0].bind_fx(&sim), Err(rh_sim::FxError::AlreadyBound));
    assert_eq!(
        asynchronous.bind_fx(&sim),
        Err(rh_sim::FxError::AlreadyBound)
    );
    assert!(empty > 0);
    assert_eq!(seats[0].pending, asynchronous.seen);
    assert!(seats[0]
        .pending
        .iter()
        .any(|e| matches!(e, SeenEvent::SignatureHit { .. })));
    assert_eq!(
        seats[0]
            .observer
            .resolve(&sim, PublicCommand::Target { actor: PlayerId(1) }),
        seats[0].observer.resolve(
            &sim,
            PublicCommand::Target {
                actor: PlayerId(999)
            }
        )
    );
    let mut foreign = sim.clone();
    foreign.step();
    feed_seats(&foreign, &mut seats);
    asynchronous.feed_fx(&foreign);
    assert!(seats[0].pending.is_empty());
    assert!(asynchronous.seen.is_empty());
    assert_eq!(
        seats[0].fault,
        Some(Fault::FxProjection(rh_sim::FxError::ForeignInstance))
    );
    assert_eq!(
        asynchronous.fault,
        Some(Fault::FxProjection(rh_sim::FxError::ForeignInstance))
    );
}
#[test]
fn synchronous_and_async_feed_do_not_depend_on_decisions_or_raw_event_presence() {
    let mut sim = scene();
    let start = MatchStart::of(&sim, PlayerId(2), 1);
    let mut seats = vec![Seat::new(
        &start,
        Box::new(scripted::Idle),
        Box::new(scripted::Idle),
    )];
    let mut asynchronous = AsyncSeat::spawn(
        &start,
        Box::new(scripted::Idle),
        Box::new(scripted::Idle),
        2,
    );
    seats[0].bind_fx(&sim).unwrap();
    asynchronous.bind_fx(&sim).unwrap();
    sim.submit(
        PlayerId(1),
        rh_sim::validate(RawCommand::Attack { a: 0., t: None }).unwrap(),
    )
    .unwrap();
    let mut empty = 0;
    for _ in 0..20 {
        let events = sim.step();
        empty += usize::from(events.is_empty());
        feed_seats(&sim, &mut seats);
        asynchronous.feed_fx(&sim);
        let count = seats[0].pending.len();
        feed_seats(&sim, &mut seats);
        asynchronous.feed_fx(&sim);
        assert_eq!(seats[0].pending.len(), count);
    }
    assert_eq!(seats[0].bind_fx(&sim), Err(rh_sim::FxError::AlreadyBound));
    assert_eq!(
        asynchronous.bind_fx(&sim),
        Err(rh_sim::FxError::AlreadyBound)
    );
    assert!(empty > 0);
    assert_eq!(seats[0].pending, asynchronous.seen);
    assert!(seats[0]
        .pending
        .iter()
        .any(|e| matches!(e, SeenEvent::SignatureHit { .. })));
    assert_eq!(
        seats[0]
            .observer
            .resolve(&sim, PublicCommand::Target { actor: PlayerId(1) }),
        seats[0].observer.resolve(
            &sim,
            PublicCommand::Target {
                actor: PlayerId(999)
            }
        )
    );
    let mut foreign = sim.clone();
    foreign.step();
    feed_seats(&foreign, &mut seats);
    asynchronous.feed_fx(&foreign);
    assert!(seats[0].pending.is_empty());
    assert!(asynchronous.seen.is_empty());
    assert_eq!(
        seats[0].fault,
        Some(Fault::FxProjection(rh_sim::FxError::ForeignInstance))
    );
    assert_eq!(
        asynchronous.fault,
        Some(Fault::FxProjection(rh_sim::FxError::ForeignInstance))
    );
}
