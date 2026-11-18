use super::*;
use crate::content::WeaponId;
use crate::results::{GvBreakdown, GvReason, Outcome, RunResult};

/// One of every [`Event`] variant. The declarations themselves are exhaustive matches (a new variant fails to compile
/// there); this list only has to hold them consistent with one another, so extend it with the variant.
fn every_event() -> Vec<Event> {
    let (a, b) = (PlayerId(1), PlayerId(2));
    let shot = crate::ProjectileLaunch {
        id: 1,
        kind: crate::ProjectileKind::Ball,
        owner: a,
        reflected: false,
        origin: crate::Vec2::new(100., 100.),
        velocity: crate::Vec2::new(380., 0.),
        time: 50.,
        range: 640.,
        target: None,
        height: 0.,
    };
    vec![
        Event::RaidStarted,
        Event::ProjectileLaunched(shot),
        Event::ProjectileEnded {
            launch: shot,
            at: shot.origin,
            reason: crate::ProjectileEnd::Hit,
        },
        Event::Windup {
            by: a,
            weapon: WeaponId::Fists,
            a: 0.0,
            ms: 1.0,
        },
        Event::Swing {
            by: a,
            weapon: WeaponId::Fists,
            x: 1.0,
            y: 2.0,
            a: 0.5,
        },
        Event::Hit {
            victim: b,
            passive: false,
            by: Some(a),
            amount: 3.0,
            weapon: Some(WeaponId::Fists),
            critical: false,
        },
        Event::Stagger { id: b },
        Event::AttackCancelled { id: b },
        Event::Block {
            id: b,
            by: a,
            weapon: WeaponId::Sword,
            passive: false,
        },
        Event::PickupRefused {
            id: b,
            reason: crate::PickupRefusal::NothingThere,
            target_visible: false,
        },
        Event::Death {
            id: b,
            x: 1.0,
            y: 2.0,
            by: Some(a),
        },
        Event::Gv {
            id: a,
            amount: 5,
            reason: GvReason::Kill,
        },
        Event::Bounty { id: a, bounty: 7 },
        Event::Victory {
            id: Some(a),
            draw: false,
        },
        Event::RaidEnded,
        Event::Resolved {
            id: a,
            bot: false,
            result: RunResult {
                faction: None,
                weapon: None,
                outcome: Outcome::Won,
                profile: "p".into(),
                duration_sec: 1,
                kills: 0,
                killed_by: None,
                killed_by_bot: false,
                place: 1,
                players: 2,
                gv: GvBreakdown::default(),
                win_bonus_withheld: false,
                win_bonus_why: None,
            },
        },
    ]
}

#[test]
fn neutral_giver_native_view_carries_no_invented_faction_or_weapon() {
    use crate::mob_runtime::{ActorOrigin, ActorProvenance, Mob};
    let sim = Sim::new(
        Default::default(),
        crate::arenas::practice_arena(),
        crate::Secrets::from_seed(5),
    );
    // Declared constructor-only fixture; no quest publication or source outcome is installed.
    let mob = Mob::born(
        &ActorProvenance {
            id: PlayerId(8),
            origin: ActorOrigin::QuestGiver {
                birth_serial: 73,
                born_tick: 0,
                quest_id: 4,
                def: 0,
            },
        },
        crate::Vec2::new(200., 300.),
        1.25,
        0.,
        crate::archetype::HitboxMode::LegacyCircle,
    );
    let view = mob_view(&sim.w, &mob, sim.server_time()).unwrap();
    assert_eq!(view.kind, ActorKind::QuestGiver);
    assert_eq!(view.faction, crate::fighter::FighterAffiliation::Neutral);
    assert_eq!(view.weapon, None);
    assert_eq!(view.mob_type, None);
    assert_eq!(
        (view.x, view.y, view.facing, view.hp_frac),
        (200., 300., 1.25, 1.)
    );
}

#[test]
fn a_sourceless_hit_on_an_unseen_victim_is_not_shown_to_an_absent_viewer() {
    let hit = Event::Hit {
        victim: PlayerId(2),
        passive: false,
        by: None,
        amount: 5.0,
        weapon: None,
        critical: false,
    };
    for me in [None, Some(PlayerId(1))] {
        let mut out = Vec::new();
        filter_events(
            std::slice::from_ref(&hit),
            me,
            |_| false,
            |_| [false; 2],
            &mut out,
        );
        assert!(out.is_empty(), "viewer {me:?} must not see it");
    }
    let mut out = Vec::new();
    filter_events(
        std::slice::from_ref(&hit),
        None,
        |_| true,
        |_| [false; 2],
        &mut out,
    );
    assert_eq!(out.len(), 1, "a seen victim's hit is shown");
}

#[test]
fn the_declarations_agree_with_what_an_observer_is_shown() {
    for e in every_event() {
        let seen = to_seen(&e, &|_| true, [true; 2]);
        // Observed exactly when it is not server-only.
        assert_eq!(
            seen.is_some(),
            e.visibility() != Visibility::ServerOnly,
            "{e:?}"
        );
        match seen {
            Some(s) => assert_eq!(s.class(), e.class(), "{e:?}"),
            None => assert_ne!(e.class(), Class::Fx, "{e:?}: fx are always observed"),
        }
    }
}
