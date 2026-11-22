//! P-BAL-PROBE-1 oracle: `Sim::quest_attempts` follows a seat's board attempt through validated inputs (accepted,
//! ready, completed; gone after its timer or an abandon), `Sim::gates_locked` follows the coliseum lock, and (feature
//! `trusted-build-setup`) `trusted_build::apply` pays Top 25 quest rewards through the board's own reward code.
use super::*;

/// The first seed (1..=64) whose board deals one of `wanted`, as a raid on that quest.
fn raid_with(wanted: &[QuestId]) -> Raid {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    for seed in 1..=64 {
        let secrets = Secrets::from_seed(seed);
        let mut stream = crate::private_rng::Sfc32::from_words(crate::quests::live::quest_salt(
            secrets.reward_salt,
        ));
        let assignment = placement::draw(&mut || stream.next_f64());
        if let Some(&quest) = wanted.iter().find(|q| assignment.contains(q)) {
            return Raid::new(new_raid(&generator, secrets), quest);
        }
    }
    panic!("no seed in 1..=64 deals any of {wanted:?}");
}

/// Quests whose objective is a set of anchors away from the giver, with a route timer: ready is seen before turn-in.
fn anchor_quests() -> [QuestId; 3] {
    [BarrowRun, Watchtowers, PilgrimsPath]
}

impl Raid {
    fn probe(&self) -> Option<crate::QuestAttemptProbe> {
        let rows = self.sim.quest_attempts(self.seat);
        assert!(
            rows.iter().filter(|r| r.offer == self.offer).count() <= 1,
            "one row per (seat, offer): {rows:?}"
        );
        rows.into_iter().find(|r| r.offer == self.offer)
    }

    fn journal_row(&mut self) -> Option<crate::quest_view::QuestRow> {
        let id = self.quest.definition().id;
        self.view().rows.into_iter().find(|r| r.def == id)
    }
}

#[test]
fn the_probe_follows_an_attempt_from_accept_through_ready_to_completion() {
    let mut raid = raid_with(&anchor_quests());
    assert!(
        raid.sim.quest_attempts(raid.seat).is_empty(),
        "an untouched board gives no rows"
    );
    raid.accept();

    let open = raid.probe().expect("the accepted attempt");
    let journal = raid
        .journal_row()
        .expect("the open offer is in the journal");
    assert_eq!(open.quest, raid.quest);
    assert!(
        open.accepted && !open.ready && !open.turned_in && !open.completed,
        "{open:?}"
    );
    assert!(
        open.deadline > raid.sim.server_time(),
        "a route timer runs: {open:?}"
    );
    assert_eq!(
        open.deadline, journal.expires_at,
        "the deadline the journal shows"
    );
    assert_eq!(u32::from(open.progress), journal.owner_progress);
    assert_eq!(open.progress, 0);

    let tokens = offer(&raid.sim.w, raid.offer).unwrap().tokens.clone();
    for &at in &tokens {
        raid.arrange(raid.seat, at);
        raid.sim.step();
    }
    let ready = raid.probe().expect("still held while ready");
    let journal = raid
        .journal_row()
        .expect("a ready offer stays in the journal");
    assert!(
        ready.accepted && ready.ready && !ready.completed,
        "{ready:?}"
    );
    assert!(journal.owner_ready);
    assert_eq!(usize::from(ready.progress), tokens.len());
    assert_eq!(u32::from(ready.progress), journal.owner_progress);
    assert_eq!(ready.deadline, journal.expires_at);
    assert!(ready.deadline > raid.sim.server_time());

    let turn_in = raid.objective(0);
    raid.arrange(raid.seat, turn_in);
    for _ in 0..4 {
        raid.sim.step();
        if is_completed(&raid.sim.w, raid.seat, raid.offer) {
            break;
        }
    }
    let done = raid.probe().expect("a completed attempt is kept");
    assert!(
        done.completed && !done.accepted && !done.ready && !done.turned_in,
        "{done:?}"
    );
    assert_eq!(done.deadline, 0., "completion clears the deadline");
    assert!(
        raid.journal_row().is_none(),
        "the journal hides the completed offer: only the probe still shows it"
    );

    assert!(
        raid.sim.quest_attempts(raid.bot).is_empty(),
        "a bot seat holds no attempt"
    );
    assert!(raid.sim.quest_attempts(PlayerId(60_000)).is_empty());
}

#[test]
fn an_attempt_that_runs_out_of_time_or_is_abandoned_leaves_no_row() {
    let base = raid_with(&anchor_quests());

    let mut timed = Raid::new(base.sim.clone(), base.quest);
    timed.accept();
    let deadline = timed.probe().expect("accepted").deadline;
    let ticks = ((deadline - timed.sim.server_time()) / timed.sim.k.tick_ms()).ceil() as usize;
    let mut locked = false;
    for _ in 0..ticks + 2 {
        timed.sim.step();
        locked |= timed.sim.w.rt.gates_locked;
    }
    assert!(
        !locked,
        "the timer ran out in the open (a lock would pause it)"
    );
    assert!(
        timed.sim.player(timed.seat).is_some_and(|p| p.standing()),
        "the seat is alive: the attempt ended by its timer, not by a death"
    );
    assert!(timed.sim.server_time() > deadline);
    assert_eq!(
        timed.probe(),
        None,
        "an expired attempt is removed from the state"
    );
    assert!(!is_completed(&timed.sim.w, timed.seat, timed.offer));

    let mut dropped = Raid::new(base.sim.clone(), base.quest);
    dropped.accept();
    assert!(dropped.probe().is_some());
    dropped.action(1, 0);
    assert_eq!(
        dropped.probe(),
        None,
        "an abandoned attempt is removed from the state"
    );
}

#[test]
fn gates_locked_follows_the_coliseum_lock() {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let mut sim = new_raid_with_patience(&generator, Secrets::from_seed(1), 1.);
    assert!(!sim.w.rt.gates_locked);
    assert!(!sim.gates_locked());
    let lock_at = sim
        .w
        .rt
        .patience_ends_at
        .expect("a first arena lock is scheduled");
    let ticks = ((lock_at - sim.w.server_time) / sim.k.tick_ms()).ceil() as usize;
    let mut seen = [false; 2];
    for _ in 0..ticks + 4 {
        sim.step();
        assert_eq!(sim.gates_locked(), sim.w.rt.gates_locked);
        seen[usize::from(sim.gates_locked())] = true;
    }
    assert_eq!(
        seen,
        [true, true],
        "the run saw the gates open, then locked"
    );
    assert!(sim.gates_locked());
}

#[cfg(feature = "trusted-build-setup")]
mod grants {
    use super::*;
    use crate::quests::board::catalog::RewardId;
    use crate::quests::board::tuning::TOP25;
    use crate::trusted_build::{apply, BuildRefusal, TrustedBuild};

    fn rewards(list: &[RewardId]) -> TrustedBuild {
        TrustedBuild {
            rewards: list.to_vec(),
            ..TrustedBuild::default()
        }
    }

    /// A checkpoint of `sim` passes `check_world` and restores to the same state hash.
    fn restores(sim: &Sim, when: &str) {
        crate::checkpoint::check_world(&sim.k, &sim.w)
            .unwrap_or_else(|e| panic!("{when}: check_world: {e}"));
        let bytes = serde_json::to_vec(sim).unwrap();
        let restored = Sim::restore(&mut serde_json::Deserializer::from_slice(&bytes))
            .unwrap_or_else(|e| panic!("{when}: restore: {e}"));
        assert_eq!(restored.state_hash(), sim.state_hash(), "{when}");
    }

    #[test]
    fn granted_spells_are_owned_observed_castable_and_restore() {
        let mut raid = raid_with(&QuestId::ALL);
        let seat = raid.seat;
        let events = raid.sim.w.pending_events.len();
        apply(
            &mut raid.sim,
            seat,
            &rewards(&[RewardId::SpellShield, RewardId::LanternWard]),
        )
        .expect("two spells into two empty slots");
        assert_eq!(
            raid.sim.w.pending_events.len(),
            events,
            "the grant adds no event"
        );

        let spells = raid.view().spells;
        let shown: Vec<_> = spells
            .iter()
            .map(|s| (s.slot, s.reward, s.charges))
            .collect();
        assert_eq!(
            shown,
            vec![
                (0, rewards::Award::SpellShield as u8, 1),
                (
                    1,
                    rewards::Award::LanternWard as u8,
                    TOP25.lantern_ward_charges
                ),
            ],
            "the seat's own observation lists both, with their charges"
        );
        assert!(spells.iter().all(|s| s.ready_at <= raid.sim.server_time()));
        restores(&raid.sim, "after a spell grant");

        let before = raid.sim.state_hash();
        assert_eq!(
            apply(&mut raid.sim, seat, &rewards(&[RewardId::Hook])),
            Err(BuildRefusal::SlotsFull(RewardId::Hook))
        );
        assert_eq!(
            raid.sim.state_hash(),
            before,
            "a refused grant changes nothing"
        );

        raid.verify_spell(rewards::Award::SpellShield);
    }

    #[test]
    fn granted_permanents_apply_their_effect() {
        use crate::mob_catalog::MobTypeId::EmberImp;
        let mut raid = raid_with(&QuestId::ALL);
        let seat = raid.seat;
        let stacks = |sim: &Sim| sim.w.player(seat).unwrap().farm.count(EmberImp);
        let damage = |sim: &Sim| {
            sim.w
                .player(seat)
                .unwrap()
                .modifiers
                .permanent_growth_inputs()
                .0
        };
        let (stacks_before, damage_before) = (stacks(&raid.sim), damage(&raid.sim));
        let events = raid.sim.w.pending_events.len();
        apply(
            &mut raid.sim,
            seat,
            &rewards(&[RewardId::FeedOrHuntDamage, RewardId::BreakersHelm]),
        )
        .expect("two permanents");
        assert_eq!(
            raid.sim.w.pending_events.len(),
            events,
            "the grant adds no event"
        );
        assert_eq!(stacks(&raid.sim), stacks_before + TOP25.feed_damage_stacks);
        assert!(
            damage(&raid.sim) > damage_before,
            "the stacks reach the fold"
        );
        assert!(rewards::has(
            &raid.sim.w,
            seat,
            rewards::Award::FeedOrHuntDamage
        ));
        assert!(rewards::has(
            &raid.sim.w,
            seat,
            rewards::Award::BreakersHelm
        ));
        assert_eq!(
            rewards::armor_bypass_fraction(&raid.sim.w, seat, WeaponId::Axe),
            TOP25.helm_bypass
        );
        assert_eq!(
            rewards::owned_spells(&raid.sim.w, seat),
            [None, None],
            "a permanent takes no spell slot"
        );
        restores(&raid.sim, "after a permanent grant");

        let before = raid.sim.state_hash();
        assert_eq!(
            apply(&mut raid.sim, seat, &rewards(&[RewardId::BreakersHelm])),
            Err(BuildRefusal::RewardHeld(RewardId::BreakersHelm))
        );
        assert_eq!(raid.sim.state_hash(), before);
    }

    #[test]
    fn refused_grants_change_nothing() {
        use crate::mob_catalog::MobTypeId::EmberImp;
        let raid = raid_with(&QuestId::ALL);
        let (seat, bot) = (raid.seat, raid.bot);
        let mut one_slot = raid.sim.clone();
        apply(&mut one_slot, seat, &rewards(&[RewardId::Hook])).expect("one empty slot left");
        let cases: Vec<(&str, Sim, PlayerId, TrustedBuild, BuildRefusal)> = vec![
            (
                "a bot seat",
                raid.sim.clone(),
                bot,
                rewards(&[RewardId::Hook]),
                BuildRefusal::BotSeat(bot),
            ),
            (
                "Crown Power",
                raid.sim.clone(),
                seat,
                rewards(&[RewardId::CrownPower]),
                BuildRefusal::RewardNeedsQuest(RewardId::CrownPower),
            ),
            (
                "Trade Up",
                raid.sim.clone(),
                seat,
                rewards(&[RewardId::TradeUp]),
                BuildRefusal::RewardNeedsQuest(RewardId::TradeUp),
            ),
            (
                "The Bargain",
                raid.sim.clone(),
                seat,
                rewards(&[RewardId::TheBargain]),
                BuildRefusal::RewardNeedsQuest(RewardId::TheBargain),
            ),
            (
                "a permanent twice in one request",
                raid.sim.clone(),
                seat,
                rewards(&[RewardId::WarmCoat, RewardId::WarmCoat]),
                BuildRefusal::RewardHeld(RewardId::WarmCoat),
            ),
            (
                "stacks and a valid spell, then a refused reward (all or nothing)",
                raid.sim.clone(),
                seat,
                TrustedBuild {
                    stacks: vec![(EmberImp, 3)],
                    rewards: vec![RewardId::Smite, RewardId::CrownPower],
                    ..TrustedBuild::default()
                },
                BuildRefusal::RewardNeedsQuest(RewardId::CrownPower),
            ),
            (
                "the Blind Rage bundle with one empty slot",
                one_slot,
                seat,
                rewards(&[RewardId::BlindRageAndStillMind]),
                BuildRefusal::SlotsFull(RewardId::BlindRageAndStillMind),
            ),
        ];
        for (why, mut sim, id, build, refusal) in cases {
            let before = sim.state_hash();
            let events = sim.w.pending_events.len();
            assert_eq!(apply(&mut sim, id, &build), Err(refusal), "{why}");
            assert_eq!(sim.state_hash(), before, "{why}: nothing changed");
            assert_eq!(sim.w.pending_events.len(), events, "{why}: no event");
        }

        let mut sim = raid.sim.clone();
        let had = sim.w.player(bot).unwrap().farm.count(EmberImp);
        apply(
            &mut sim,
            bot,
            &TrustedBuild {
                stacks: vec![(EmberImp, 3)],
                ..TrustedBuild::default()
            },
        )
        .expect("a request without rewards still serves a bot seat");
        assert_eq!(sim.w.player(bot).unwrap().farm.count(EmberImp), had + 3);
    }
}
