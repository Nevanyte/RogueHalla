//! Lobby pick, profile construction and the training admission gate.

use super::*;

#[test]
fn the_fullest_open_lobby_is_picked_and_ties_go_to_the_lowest_id() {
    let m = rooms(&[
        (9, Phase::Lobby, 2, 0),
        (4, Phase::Lobby, 5, 0),
        (7, Phase::Lobby, 5, 0),
        (1, Phase::Lobby, 1, 0),
    ]);
    assert_eq!(
        open_lobby(&m, rh_sim::constants::MAX_PLAYERS),
        Some(id(4)),
        "fullest first, tie to the lowest id"
    );
    let m = rooms(&[(3, Phase::Lobby, 0, 0), (2, Phase::Lobby, 0, 0)]);
    assert_eq!(open_lobby(&m, rh_sim::constants::MAX_PLAYERS), Some(id(2)));
    assert_eq!(
        open_lobby(&HashMap::new(), rh_sim::constants::MAX_PLAYERS),
        None
    );
}

#[test]
fn the_pick_does_not_depend_on_hash_map_order() {
    // The same rooms inserted in every rotation iterate differently (the map is randomly seeded per instance);
    // the pick is always the same.
    let spec = [
        (5, Phase::Lobby, 3, 1),
        (2, Phase::Lobby, 4, 0),
        (8, Phase::Active, 6, 0),
        (6, Phase::Lobby, 4, 0),
        (1, Phase::Lobby, 0, 2),
    ];
    for rot in 0..spec.len() {
        let mut s = spec.to_vec();
        s.rotate_left(rot);
        for _ in 0..8 {
            assert_eq!(
                open_lobby(&rooms(&s), rh_sim::constants::MAX_PLAYERS),
                Some(id(2))
            );
        }
    }
}

#[test]
fn a_lobby_counts_its_reservations_and_runs_and_full_lobbies_are_not_offered() {
    // 6 seated + 2 reserved = full: not offered although only 6 are seated; the emptier lobby is.
    let m = rooms(&[(1, Phase::Lobby, 6, 2), (2, Phase::Lobby, 1, 0)]);
    assert_eq!(open_lobby(&m, rh_sim::constants::MAX_PLAYERS), Some(id(2)));
    // A lobby that closed into its run never is, however empty.
    let m = rooms(&[(1, Phase::Active, 0, 0), (2, Phase::Ended, 0, 0)]);
    assert_eq!(open_lobby(&m, rh_sim::constants::MAX_PLAYERS), None);
    // Reservations alone make a lobby fuller than its rival.
    let m = rooms(&[(1, Phase::Lobby, 1, 0), (2, Phase::Lobby, 0, 3)]);
    assert_eq!(open_lobby(&m, rh_sim::constants::MAX_PLAYERS), Some(id(2)));
}

/// Protocol 8 admits the source lifecycle transport, so the director may be built for it; the full Wish profile is
/// still refused before any runtime spawn.
#[tokio::test]
async fn arena_profile_director_construction_is_admitted_transport() {
    let sim = SimConfig {
        quests: false,
        consumables: true,
        lifecycle: rh_sim::ArenaMode::Source(Default::default()),
        ..Default::default()
    };
    assert!(crate::wire::profile_supported(&sim).is_ok());
    let director = Director::new(
        DirectorConfig::new(sim, rh_sim::arenas::practice_arena()),
        Arc::new(Persistence::Memory(Box::<MemoryStore>::default())),
        Arc::new(Auth::Dev),
        Arc::new(Lifecycle::default()),
    );
    drop(director);
}

#[tokio::test]
async fn explicitly_configured_full_wish_profile_is_admitted_by_director() {
    let sim = SimConfig {
        consumables: true,
        abilities: rh_sim::abilities::Mode::Full,
        lifecycle: rh_sim::ArenaMode::Off,
        ..Default::default()
    };
    let director = Director::new(
        DirectorConfig::new(sim, rh_sim::arenas::practice_arena()),
        Arc::new(Persistence::Memory(Box::<MemoryStore>::default())),
        Arc::new(Auth::Dev),
        Arc::new(Lifecycle::default()),
    );
    drop(director);
}

#[test]
fn training_admission_rejects_incompatible_host_configuration() {
    let args = vec!["--runtime".into(), "training".into()];
    let settings = crate::runtime::Settings::from_args(&args).unwrap().unwrap();
    let mut cfg = DirectorConfig::new(settings.sim(), settings.arena().unwrap());
    assert!(validate_runtime(&cfg).is_ok());
    cfg.fill_bots_to = 1;
    assert!(validate_runtime(&cfg).is_err());
    cfg.fill_bots_to = 0;
    cfg.record = true;
    assert!(validate_runtime(&cfg).is_err());
    cfg.record = false;
    cfg.sim.lifecycle = rh_sim::ArenaMode::Source(Default::default());
    assert!(validate_runtime(&cfg).is_err());
}
