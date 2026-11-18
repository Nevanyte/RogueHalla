//! Fill policy reaching a spawned room, and bot results reaching the record caller.

use super::*;

#[tokio::test(start_paused = true)]
async fn the_configured_fill_policy_reaches_a_spawned_room_with_the_room_s_fill_count() {
    let mut cfg = DirectorConfig::new(SimConfig::default(), rh_sim::arenas::practice_arena());
    cfg.bot_fill = Some(crate::bot_fill::BotFillConfig {
        fill_to: 99,
        arrival_seconds: 3.,
        ..crate::bot_fill::BotFillConfig::default()
    });
    let d = Director::new(
        cfg,
        Arc::new(Persistence::Memory(Box::<MemoryStore>::default())),
        Arc::new(Auth::Dev),
        Arc::new(Lifecycle::default()),
    );
    let fill = d
        .fill_policy(4)
        .expect("the configured fill reaches the room");
    assert_eq!(
        fill.fill_to, 4,
        "the room's fill count overrides the default"
    );
    assert_eq!(fill.arrival_seconds, 3.);
    assert!(
        director().fill_policy(4).is_none(),
        "without a configured fill the room keeps its placeholder fill"
    );
}

/// P-BOTS-5 remainder: the director is the record caller (BotManager.onResult), reads the live store's bias once
/// per lobby (fill_policy), and hands a bot's resolved run over with the spec facts the room had.
#[tokio::test(start_paused = true)]
async fn bot_results_reach_the_record_caller_and_a_new_lobby_reads_the_refreshed_bias() {
    use rh_policy::bots::learning::Tier;
    use rh_policy::bots::{PersonaId, TierId};

    #[derive(Default)]
    struct ProbeSink {
        runs: std::sync::Mutex<Vec<BotRunFacts>>,
        bias: rh_policy::bots::learning::LearnedBias,
    }
    impl LearningSink for ProbeSink {
        fn snapshot(&self) -> rh_policy::bots::learning::LearnedBias {
            self.bias.clone()
        }
        fn record(&self, facts: BotRunFacts) {
            self.runs.lock().unwrap().push(facts);
        }
    }
    let mut cfg = DirectorConfig::new(SimConfig::default(), rh_sim::arenas::practice_arena());
    cfg.bot_fill = Some(crate::bot_fill::BotFillConfig::with_fill_to(4));
    let d = Director::new(
        cfg,
        Arc::new(Persistence::Memory(Box::<MemoryStore>::default())),
        Arc::new(Auth::Dev),
        Arc::new(Lifecycle::default()),
    );
    let mut biases = std::collections::BTreeMap::new();
    biases.insert("faction|hunter|plague".to_string(), 0.25);
    let probe = Arc::new(ProbeSink {
        runs: std::sync::Mutex::new(Vec::new()),
        bias: rh_policy::bots::learning::LearnedBias::with_biases(biases),
    });
    d.install_learning(probe.clone());
    // The next lobby's fill config reads the store's snapshot (here: the probe's).
    let fill = d.fill_policy(4).expect("configured fill");
    assert!(fill.learned.enabled());
    assert_eq!(fill.learned.bias("faction|hunter|plague"), 0.25);
    // A resolved fill bot's report becomes the learning summary (outcome/place/players + the spec's
    // tier/persona/faction).
    let spec = crate::bot_fill::BotSeatSpec {
        number: 1,
        name: "Wren".into(),
        tier: TierId::Hard,
        persona: PersonaId::Hunter,
        faction: FactionId::Plague,
        seed: 5,
    };
    let mut result = some_result("Wren");
    result.outcome = rh_sim::Outcome::Died;
    result.place = 2;
    result.players = 4;
    d.reports
        .send(RoomReport::Result {
            bot_choices: Vec::new(),
            admission: None,
            match_id: id(1),
            profile: "Wren".into(),
            bot: true,
            bot_spec: Some(spec),
            fence: None,
            result,
            roster: Roster::default(),
        })
        .unwrap();
    settle().await;
    let runs = probe.runs.lock().unwrap();
    assert_eq!(runs.len(), 1, "one resolved bot run is recorded");
    assert_eq!(
        runs[0],
        BotRunFacts {
            tier: Tier::Hard,
            persona: PersonaId::Hunter,
            faction: FactionId::Plague,
            outcome: rh_policy::bots::learning::RunOutcome::Died,
            place: 2,
            players: 4,
        }
    );
    assert_eq!(runs[0].faction_key(), "faction|hunter|plague");
}
