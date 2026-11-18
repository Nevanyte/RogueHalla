use super::*;
use rh_sim::RawCommand;
fn scene() -> Sim {
    Sim::reset_active(
        rh_sim::SimConfig {
            quests: false,
            faction_mode: rh_sim::FactionMode::EngagementHolyPlagueFireFrostSentenced,
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        rh_sim::Secrets::from_seed(12),
        &(0..3)
            .map(|i| rh_sim::Placement {
                profile: format!("p{i}"),
                faction: if i == 0 {
                    rh_sim::FactionId::Sentenced
                } else {
                    rh_sim::FactionId::Holy
                },
                bot: false,
                at: rh_sim::Vec2::new(400. + i as f64 * 56., 320.),
                hp: None,
                weapon0: None,
                weapon1: None,
            })
            .collect::<Vec<_>>(),
    )
}
#[test]
fn sentenced_synchronous_async_feed_retains_every_step() {
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
