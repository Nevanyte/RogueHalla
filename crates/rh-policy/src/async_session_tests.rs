use super::*;
use rh_sim::{FactionId, Placement, Secrets, SimConfig, Vec2};
struct Controlled {
    received: mpsc::Sender<u32>,
    release: mpsc::Receiver<()>,
}
impl Policy for Controlled {
    fn spec(&self) -> PolicySpec {
        let mut spec = scripted::Idle.spec();
        spec.decision_interval_ticks = 100;
        spec
    }
    fn reset(&mut self, _: &MatchStart) {}
    fn decide(&mut self, obs: &Observation) -> Vec<PublicCommand> {
        self.received.send(obs.tick).unwrap();
        self.release.recv().unwrap();
        vec![PublicCommand::Move {
            x: 800.,
            y: 320.,
            seq: None,
        }]
    }
}
fn seat(sim: &Sim) -> (AsyncSeat, mpsc::Receiver<u32>, mpsc::Sender<()>) {
    let (sent, received) = mpsc::channel();
    let (release, wait) = mpsc::channel();
    let mut seat = AsyncSeat::spawn(
        &MatchStart::of(sim, PlayerId(1), 7),
        Box::new(Controlled {
            received: sent,
            release: wait,
        }),
        Box::new(scripted::Idle),
        20,
    );
    seat.bind_fx(sim).unwrap();
    (seat, received, release)
}
#[test]
#[allow(clippy::disallowed_methods)] // Harness timeout for a real worker handshake, never a policy decision.
fn a_real_late_worker_reply_cannot_cross_a_faulted_session() {
    let mut original = Sim::reset_active(
        SimConfig::default(),
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(5),
        &(0..2)
            .map(|i| Placement {
                profile: format!("p{i}"),
                faction: FactionId::Holy,
                bot: false,
                at: Vec2::new(400. + 800. * i as f64, 320.),
                hp: None,
                weapon0: None,
                weapon1: None,
            })
            .collect::<Vec<_>>(),
    );
    let (mut old, received, release) = seat(&original);
    old.poll(&mut original, &[], |_, _| panic!("worker is held"))
        .unwrap();
    assert_eq!(received.recv_timeout(Duration::from_secs(5)).unwrap(), 1);
    assert_eq!(old.pending.as_ref().unwrap().tick, 1);
    assert_eq!(old.next_tick, 101);
    let mut foreign = original.clone();
    foreign.step();
    let before_hash = foreign.state_hash();
    let refused = Err(OfferError::FxProjection(rh_sim::FxError::ForeignInstance));
    assert_eq!(
        old.poll(&mut foreign, &[], |_, _| panic!("fault cannot submit")),
        refused
    );
    assert!(old.pending.is_none());
    release.send(()).unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    while old.late == 0 {
        assert!(
            Instant::now() < deadline,
            "real worker reply did not arrive"
        );
        assert_eq!(
            old.poll(&mut foreign, &[], |_, _| panic!("stale reply submitted")),
            refused
        );
        std::thread::yield_now();
    }
    assert_eq!(
        foreign.state_hash(),
        before_hash,
        "fault/polls queued no input"
    );
    assert_eq!(old.stats.accepted, 0);
    assert_eq!(old.stats.fallback_decisions, 0);
    assert_eq!(
        old.next_tick, 101,
        "failure did not advance decision cadence"
    );
    let before = foreign.player(PlayerId(1)).unwrap().pos();
    foreign.step();
    assert_eq!(
        foreign.player(PlayerId(1)).unwrap().pos(),
        before,
        "stale move must not execute"
    );
    // A new seat bound to this actual instance accepts its own real reply between decision ticks.
    let (mut fresh, received, release) = seat(&foreign);
    fresh
        .poll(&mut foreign, &[], |_, _| panic!("fresh worker is held"))
        .unwrap();
    assert_eq!(
        received.recv_timeout(Duration::from_secs(5)).unwrap(),
        foreign.tick()
    );
    release.send(()).unwrap();
    let deadline = Instant::now() + Duration::from_secs(5);
    let mut submitted = 0;
    while fresh.pending.is_some() {
        assert!(Instant::now() < deadline);
        fresh
            .poll(&mut foreign, &[], |_, _| submitted += 1)
            .unwrap();
        std::thread::yield_now();
    }
    assert_eq!(submitted, 1);
    assert_eq!(fresh.stats.accepted, 1);
    assert_eq!(fresh.fault, None);
    foreign.step();
    assert_eq!(foreign.player(PlayerId(1)).unwrap().body.x, before.x + 8.5);
}
