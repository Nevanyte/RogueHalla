use super::*;
use crate::scripted::{Brawler, Idle};
use rh_sim::{FactionId, Placement, Secrets, SimConfig, Vec2};
use std::sync::Mutex;

type Seen = Arc<Mutex<Vec<(PlayerId, u64, usize, u64, u16, f64)>>>;

fn sim() -> Sim {
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
        SimConfig::default(),
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(1),
        &[place],
    )
}

/// A policy that records what it was told at reset and declares any versions.
struct Probe {
    contract: u16,
    observation_version: u16,
    seen: Seen,
}
impl Policy for Probe {
    fn spec(&self) -> PolicySpec {
        PolicySpec {
            name: "probe".into(),
            version: "1".into(),
            contract: self.contract,
            observation_version: self.observation_version,
            decision_interval_ticks: 1,
        }
    }
    fn reset(&mut self, m: &MatchStart) {
        self.seen.lock().unwrap().push((
            m.me,
            m.seed,
            m.arena.spawns.len(),
            m.content_hash,
            m.observation_version,
            m.tick_ms,
        ));
    }
    fn decide(&mut self, _: &Observation) -> Vec<PublicCommand> {
        Vec::new()
    }
}

fn probe(contract: u16, observation_version: u16) -> (Probe, Seen) {
    let seen = Seen::default();
    (
        Probe {
            contract,
            observation_version,
            seen: Arc::clone(&seen),
        },
        seen,
    )
}

#[test]
fn reset_receives_the_public_match_context() {
    let sim = sim();
    let me = sim.players()[0].id;
    let m = MatchStart::of(&sim, me, 77);
    let (p, seen) = probe(POLICY_CONTRACT_VERSION, OBSERVATION_VERSION);
    let seat = Seat::new(&m, Box::new(p), Box::new(Idle));
    assert!(seat.fault.is_none());
    let seen = seen.lock().unwrap();
    assert_eq!(seen.len(), 1);
    assert_eq!(
        seen[0],
        (
            me,
            77,
            sim.arena().spawns.len(),
            rh_sim::content::content_hash(),
            OBSERVATION_VERSION,
            rh_sim::constants::TICK_MS
        )
    );
    assert!(Arc::ptr_eq(&m.arena, sim.shared_arena()));
}

#[test]
fn a_seat_refuses_a_policy_of_another_contract_or_observation_version() {
    let sim = sim();
    let m = MatchStart::of(&sim, sim.players()[0].id, 1);
    for contract in [POLICY_CONTRACT_VERSION - 1, POLICY_CONTRACT_VERSION + 1] {
        let (p, _) = probe(contract, OBSERVATION_VERSION);
        let seat = Seat::new(&m, Box::new(p), Box::new(Idle));
        assert_eq!(seat.fault, Some(Fault::IncompatibleContract { contract }));
    }
    for version in [OBSERVATION_VERSION - 1, OBSERVATION_VERSION + 1] {
        let (p, _) = probe(POLICY_CONTRACT_VERSION, version);
        let seat = Seat::new(&m, Box::new(p), Box::new(Idle));
        assert_eq!(seat.fault, Some(Fault::IncompatibleObservation { version }));
    }
    assert!(Seat::new(&m, Box::new(Brawler::new(2)), Box::new(Idle))
        .fault
        .is_none());
}

#[test]
#[allow(clippy::disallowed_methods)] // Wall-clock timeout only in the test harness polling a real worker.
fn async_worker_and_fallback_resolve_only_actually_delivered_offers() {
    struct Click {
        log: mpsc::Sender<rh_sim::PickupHandle>,
        first: bool,
        incompatible: bool,
    }
    impl Policy for Click {
        fn spec(&self) -> PolicySpec {
            PolicySpec {
                contract: POLICY_CONTRACT_VERSION + u16::from(self.incompatible),
                decision_interval_ticks: 25,
                ..Idle.spec()
            }
        }
        fn reset(&mut self, _: &MatchStart) {}
        fn decide(&mut self, obs: &Observation) -> Vec<PublicCommand> {
            let handle = obs.props.iter().find(|p| p.cue.focused).unwrap().handle;
            self.log.send(handle).unwrap();
            if std::mem::take(&mut self.first) {
                return Vec::new();
            }
            vec![PublicCommand::Pickup { handle }]
        }
    }
    for use_fallback in [true, false] {
        let position = Vec2::new(100., 272.);
        let p = Placement {
            profile: "worker".into(),
            faction: FactionId::Holy,
            bot: false,
            at: position,
            hp: None,
            weapon0: None,
            weapon1: None,
        };
        let second = Placement {
            profile: "second".into(),
            at: Vec2::new(1300., 272.),
            ..p.clone()
        };
        let mut sim = Sim::reset_active(
            SimConfig::default(),
            rh_sim::arenas::practice_arena(),
            Secrets::from_seed(3),
            &[p, second],
        );
        sim.seed_weapon_container(
            rh_sim::ContainerKind::Ground,
            Vec2::new(1300., 272.),
            &[rh_sim::WeaponId::Bow],
        )
        .unwrap();
        sim.seed_weapon_container(
            rh_sim::ContainerKind::Ground,
            position,
            &[rh_sim::WeaponId::Axe],
        )
        .unwrap();
        let (tx, rx) = mpsc::channel();
        let click = |incompatible| Click {
            log: tx.clone(),
            first: true,
            incompatible,
        };
        let me = PlayerId(1);
        let mut seat = AsyncSeat::spawn(
            &MatchStart::of(&sim, me, 3),
            Box::new(click(use_fallback)),
            Box::new(click(false)),
            100,
        );
        seat.poll(&mut sim, &[], |_, _| panic!("no first action"))
            .unwrap();
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap().get(), 1);
        let start = Instant::now();
        while seat.pending.is_some() {
            assert!(start.elapsed() < Duration::from_secs(2));
            std::thread::yield_now();
            seat.poll(&mut sim, &[], |_, _| panic!("no first action"))
                .unwrap();
        }
        for _ in 1..25 {
            sim.step();
            seat.poll(&mut sim, &[], |_, _| panic!("no skipped-tick action"))
                .unwrap();
            assert_eq!(
                seat.observer
                    .resolve(
                        &sim,
                        PublicCommand::Pickup {
                            handle: rh_sim::PickupHandle::new(2).unwrap()
                        }
                    )
                    .unwrap(),
                rh_sim::validate(rh_sim::RawCommand::MissingPickup).unwrap()
            );
        }
        sim.step();
        seat.poll(&mut sim, &[], |_, _| {}).unwrap();
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap().get(), 2);
        // Worker result delivery is asynchronous. Poll this frozen tick until the accepted answer
        // arrives, bounded so a regression cannot hang the test suite.
        let start = Instant::now();
        while seat.stats.accepted == 0 {
            assert!(start.elapsed() < Duration::from_secs(2));
            std::thread::yield_now();
            seat.poll(&mut sim, &[], |_, _| {}).unwrap();
        }
        sim.step();
        assert_eq!(
            sim.player(me).unwrap().weapon.weapon0,
            Some(rh_sim::WeaponId::Axe)
        );
    }
}

#[test]
fn a_late_or_panicked_async_answer_uses_the_observed_fallback() {
    for late in [true, false] {
        let mut sim = sim();
        let m = MatchStart::of(&sim, sim.players()[0].id, 1);
        let mut seat = AsyncSeat::spawn(&m, Box::new(Idle), Box::new(Idle), 0);
        // Feed a controlled completion; no scheduling race in the deadline/panic assertions.
        let (tx, rx) = mpsc::channel();
        seat.rx = rx;
        let asked = RequestToken {
            session: seat.delivery.identity(),
            epoch: crate::bots::delivered::Epoch(1),
            sample: delivery::SampleId(1),
            tick: sim.tick(),
        };
        seat.pending = Some(asked.clone());
        seat.next_tick = asked.tick + 100;
        if late {
            tx.send((asked, Ok(vec![PublicCommand::Stop { seq: None }])))
                .unwrap();
            sim.step();
        } else {
            tx.send((asked, Err(WorkerFailure::Panicked))).unwrap();
        }
        seat.poll(&mut sim, &[], |_, _| panic!("bad worker answer submitted"))
            .unwrap();
        assert_eq!(seat.stats.fallback_decisions, 1);
        assert_eq!(seat.late, u64::from(late));
        assert_eq!(seat.fault, (!late).then_some(Fault::Panicked));
    }
}

#[test]
fn an_async_seat_faults_and_plays_its_fallback_on_a_mismatch() {
    let mut sim = sim();
    let m = MatchStart::of(&sim, sim.players()[0].id, 1);
    let (p, _) = probe(POLICY_CONTRACT_VERSION, OBSERVATION_VERSION + 1);
    let mut seat = AsyncSeat::spawn(&m, Box::new(p), Box::new(Idle), 3);
    assert_eq!(
        seat.fault,
        Some(Fault::IncompatibleObservation {
            version: OBSERVATION_VERSION + 1
        })
    );
    seat.poll(&mut sim, &[], |_, _| panic!("idle fallback submitted"))
        .unwrap();
    assert_eq!(seat.stats.fallback_decisions, 1);
    let ok = AsyncSeat::spawn(&m, Box::new(Brawler::new(1)), Box::new(Idle), 3);
    assert!(ok.fault.is_none());
}
