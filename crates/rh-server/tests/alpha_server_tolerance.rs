//! P-ALPHA-SERVER-1 oracle: disconnect tolerance for browser testers (a hidden tab stops pinging) and the lifecycle
//! log line format. See docs/backlog/packets/P-ALPHA-SERVER-1.md.

use rh_protocol::RejectCode;
use rh_server::director::{DirectorConfig, LEAVING_WAIT};
use rh_server::lifecycle_log::{self as log, DropReason};
use rh_sim::{Arena, SimConfig, TileMap, Vec2};
use std::net::IpAddr;
use std::time::Duration;

fn sim_config() -> SimConfig {
    SimConfig {
        hitboxes: Default::default(),
        mob_spacing: Default::default(),
        regen_in_combat: rh_sim::RegenInCombat::Legacy,
        camp_buffs: rh_sim::CampBuffRule::Legacy,
        frost_wish: rh_sim::FrostWish::Freeze,
        walk_hints: None,
        regen: false,
        quests: false,
        top25_quests: false,
        training: false,
        dummies: false,
        consumables: true,
        abilities: rh_sim::abilities::Mode::Off,
        wish_lock_ms: rh_sim::abilities::WISH_LOCK_MS,
        lifecycle: rh_sim::ArenaMode::Off,
        hazards: rh_sim::terrain::Mode::Off,
        mob_mode: rh_sim::mob_runtime::MobMode::Off,
        initial_loot: false,
        min_players: 2,
        lobby_seconds: 30.0,
        fill_seconds: 0.0,
        pvp_rules: true,
        weapon_specials: true,
        faction_mode: rh_sim::FactionMode::Off,
        tick_ms: 50,
    }
}

fn arena() -> Arena {
    let rows: Vec<String> = (0..8)
        .map(|y| {
            (0..12)
                .map(|x| {
                    if x == 0 || y == 0 || x == 11 || y == 7 {
                        '#'
                    } else {
                        '.'
                    }
                })
                .collect()
        })
        .collect();
    Arena {
        map: TileMap::from_rows(&rows).unwrap(),
        spawns: vec![Vec2::new(0.0, 0.0), Vec2::new(0.0, 0.0)],
    }
}

#[test]
fn idle_timeout_tolerates_a_throttled_background_tab() {
    assert_eq!(rh_server::conn::IDLE_TIMEOUT, Duration::from_secs(30));
}

#[test]
fn reconnect_grace_holds_a_seat_for_a_minute() {
    assert_eq!(rh_server::room::RECONNECT_GRACE, Duration::from_secs(60));
}

#[test]
fn leaving_wait_covers_a_quick_rejoin_after_match_end() {
    assert_eq!(LEAVING_WAIT, Duration::from_secs(6));
}

#[test]
fn director_defaults_pick_up_the_tolerant_values() {
    let cfg = DirectorConfig::new(sim_config(), arena());
    assert_eq!(cfg.idle_timeout, Duration::from_secs(30));
    assert_eq!(cfg.reconnect_grace, Duration::from_secs(60));
}

const ROOM: &str = "00112233445566778899aabbccddeeff";

#[test]
fn generic_line_has_prefix_event_and_ordered_fields() {
    assert_eq!(
        log::line("x", &[("a", "1"), ("b", "two")]),
        "[server] INFO event=x a=1 b=two"
    );
    assert_eq!(log::line("x", &[]), "[server] INFO event=x");
}

#[test]
fn generic_line_quotes_values_that_would_break_parsing() {
    assert_eq!(
        log::line(
            "x",
            &[("a", "has space"), ("b", ""), ("c", "k=v"), ("d", "q\"q")]
        ),
        "[server] INFO event=x a=\"has space\" b=\"\" c=\"k=v\" d=\"q\\\"q\""
    );
}

#[test]
fn every_lifecycle_event_has_its_documented_line() {
    let peer: IpAddr = "203.0.113.7".parse().unwrap();
    let p = "[server] INFO event=";
    assert_eq!(
        log::join(ROOM, "alice", 4),
        format!("{p}join room={ROOM} callsign=alice humans=4")
    );
    assert_eq!(
        log::resume(ROOM, "alice", 4),
        format!("{p}resume room={ROOM} callsign=alice humans=4")
    );
    assert_eq!(
        log::grace_expired(ROOM, "alice"),
        format!("{p}grace_expired room={ROOM} callsign=alice")
    );
    assert_eq!(
        log::leave(ROOM, "alice", 3),
        format!("{p}leave room={ROOM} callsign=alice humans=3")
    );
    assert_eq!(
        log::reject(RejectCode::ProfileInUse, "profile in use", peer),
        format!("{p}reject code=ProfileInUse reason=\"profile in use\" peer=203.0.113.7")
    );
    assert_eq!(
        log::match_end(ROOM, Duration::from_millis(93_250), 5),
        format!("{p}match_end room={ROOM} duration_ms=93250 humans=5")
    );
}

#[test]
fn drop_line_names_the_reason() {
    for (reason, word) in [
        (DropReason::IdleTimeout, "idle_timeout"),
        (DropReason::Closed, "closed"),
        (DropReason::Replaced, "replaced"),
    ] {
        assert_eq!(
            log::dropped(ROOM, "alice", reason),
            format!("[server] INFO event=drop room={ROOM} callsign=alice reason={word}")
        );
    }
}

#[test]
fn a_callsign_with_spaces_stays_one_field() {
    let l = log::join(ROOM, "Bob the Bold", 2);
    assert!(l.contains("callsign=\"Bob the Bold\""), "{l}");
    assert!(!l.contains('\n'));
}
