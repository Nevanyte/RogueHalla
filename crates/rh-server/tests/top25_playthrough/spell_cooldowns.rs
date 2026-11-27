//! Encoded-room quest transport: cooldowns survive spell drop and pickup.
use super::*;

fn quest_row<'a>(client: &'a Client, def: &str) -> Option<&'a rh_protocol::WireQuestRow> {
    client.board.rows.iter().find(|row| row.def == def)
}

fn quest_view_from_frame(frame: &[u8]) -> WireQuestView {
    let ServerMsg::State { items, .. } = ServerMsg::decode(frame).unwrap() else {
        panic!("expected state frame")
    };
    items
        .into_iter()
        .find_map(|item| match item {
            StateItem::Quests(Some(view)) => Some(view),
            _ => None,
        })
        .expect("frame contains a public quest view")
}

fn move_a_to(room: &mut Room, a: &mut Client, b: &mut Client, at: WireQuestPoint, label: &str) {
    a.move_to(room, at);
    until(room, a, b, 1200, label, |room, a, _| {
        let pos = a.pos(room);
        (pos.x - at.x).hypot(pos.y - at.y) < 24.
    });
    a.input(
        room,
        WireCommand::Stop {
            seq: Some((a.seq as u8).wrapping_add(1)),
        },
    );
    wake(room, a, b);
}

fn accept_a_quest(
    room: &mut Room,
    a: &mut Client,
    b: &mut Client,
    def: &str,
) -> (rh_protocol::QuestHandle, WireQuestPoint) {
    let row = quest_row(a, def).unwrap_or_else(|| panic!("board has no {def} offer"));
    let (handle, giver) = (row.handle, row.giver);
    move_a_to(room, a, b, giver, &format!("reach {def} giver"));
    until(room, a, b, 4, "offer is actionable", |_, a, _| {
        quest_row(a, def).is_some_and(|row| row.can_accept)
    });
    a.input(room, WireCommand::QuestAccept { handle });
    until(room, a, b, 8, "offer accepted", |_, a, _| {
        quest_row(a, def).is_some_and(|row| row.owner_accepted)
    });
    (handle, giver)
}

#[tokio::test(start_paused = true)]
async fn public_quest_frame_keeps_zero_charge_spell_and_drop_reacquire_valid() {
    use rh_protocol::WireQuestAction;

    let secrets = Secrets::from_seed(1);
    let mut room = super::test_room(
        secrets,
        0..32,
        [5; 16],
        ArenaRules {
            plan_seconds: 3.,
            first_patience_seconds: None,
            patience_seconds: 900.,
            patience_min_seconds: 600.,
            coliseum_schedule: None,
            ..Default::default()
        },
        50,
    );
    let mut a = Client::join(&mut room, "spell-owner", 1);
    let mut b = Client::join(&mut room, "other-seat", 2);
    until(
        &mut room,
        &mut a,
        &mut b,
        20,
        "fixed seed board during planning",
        |room, a, b| {
            room.sim().phase() == Phase::Planning
                && a.board.rows.len() == 5
                && b.board.rows.len() == 5
        },
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        80,
        "raid must start",
        |room, a, _| {
            room.sim().phase() == Phase::Active
                && quest_row(a, "ember_walk").is_some()
                && quest_row(a, "two_banners").is_some()
                && quest_row(a, "barrow_run").is_some()
        },
    );

    // Complete Ember Walk from the public objective markers and current public action choices.
    let (ember_handle, ember_giver) = accept_a_quest(&mut room, &mut a, &mut b, "ember_walk");
    let ember_points: Vec<_> = a
        .board
        .objectives
        .iter()
        .filter(|objective| objective.quest == ember_handle)
        .map(|objective| objective.at)
        .collect();
    assert_eq!(ember_points.len(), 3);
    for (index, at) in ember_points.into_iter().enumerate() {
        let target = index as u8;
        move_a_to(&mut room, &mut a, &mut b, at, "reach Ember Walk objective");
        until(
            &mut room,
            &mut a,
            &mut b,
            4,
            "token action is publicly offered",
            |_, a, _| {
                a.board.actions.iter().any(|action| {
                    action.quest == ember_handle && action.action == 3 && action.target == target
                })
            },
        );
        a.input(
            &mut room,
            WireCommand::QuestInteract {
                handle: ember_handle,
                action: 3,
                target,
            },
        );
        until(
            &mut room,
            &mut a,
            &mut b,
            4,
            "token progress is publicly published",
            |_, a, _| {
                quest_row(a, "ember_walk").is_some_and(|row| row.owner_progress > u32::from(target))
            },
        );
    }
    move_a_to(
        &mut room,
        &mut a,
        &mut b,
        ember_giver,
        "return Ember Walk to its giver",
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        20,
        "Spell Shield reward is published",
        |_, a, _| {
            quest_row(a, "ember_walk").is_none()
                && a.board
                    .spells
                    .iter()
                    .any(|spell| spell.reward == RewardId::SpellShield as u8 && spell.charges == 1)
        },
    );
    // Fill the second slot with Warhorn. We will spend and replace the shield only after the
    // third route is complete, keeping its original cooldown active through drop and pickup.
    let (banner_handle, _banner_giver) = accept_a_quest(&mut room, &mut a, &mut b, "two_banners");
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "banner branch choices are public",
        |_, a, _| {
            a.board.actions.iter().any(|action| {
                action.quest == banner_handle && action.action == 2 && action.target == 0
            })
        },
    );
    a.input(
        &mut room,
        WireCommand::QuestInteract {
            handle: banner_handle,
            action: 2,
            target: 0,
        },
    );
    wake(&mut room, &mut a, &mut b);
    let banner_end = a
        .board
        .objectives
        .iter()
        .find(|objective| objective.quest == banner_handle)
        .expect("chosen banner route has a public endpoint")
        .at;
    move_a_to(
        &mut room,
        &mut a,
        &mut b,
        banner_end,
        "reach selected banner endpoint",
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        20,
        "Warhorn reward fills second slot",
        |_, a, _| {
            quest_row(a, "two_banners").is_none()
                && a.board
                    .spells
                    .iter()
                    .any(|spell| spell.reward == RewardId::Warhorn as u8)
        },
    );
    assert_eq!(a.board.spells.len(), 2);
    let (barrow_handle, barrow_giver) = accept_a_quest(&mut room, &mut a, &mut b, "barrow_run");
    let barrow_points: Vec<_> = a
        .board
        .objectives
        .iter()
        .filter(|objective| objective.quest == barrow_handle)
        .map(|objective| objective.at)
        .collect();
    assert!(!barrow_points.is_empty());
    for at in barrow_points {
        move_a_to(&mut room, &mut a, &mut b, at, "visit Barrow Run marker");
    }
    move_a_to(
        &mut room,
        &mut a,
        &mut b,
        barrow_giver,
        "return Barrow Run to its giver",
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        12,
        "Barrow Run reward choice is offered",
        |_, a, _| {
            a.board.actions.iter().any(|action| {
                action.quest == barrow_handle && action.action == 8 && action.target == 0
            })
        },
    );
    // Give Warhorn a visible enemy at the turn-in before starting the shield cooldown.
    // Both seats travel using the same encoded movement input as a player.
    move_a_to(
        &mut room,
        &mut b,
        &mut a,
        barrow_giver,
        "bring the other seat into Warhorn range",
    );
    let shield = a
        .board
        .spells
        .iter()
        .find(|spell| spell.reward == RewardId::SpellShield as u8)
        .copied()
        .unwrap();
    assert_eq!(shield.slot, 0);
    let pos = a.pos(&room);
    a.input(
        &mut room,
        WireCommand::Cast {
            slot: shield.slot + 1,
            x: pos.x as f32,
            y: pos.y as f32,
            actor: None,
            t: None,
            seq: None,
        },
    );
    wake(&mut room, &mut a, &mut b);
    let spent = a
        .board
        .spells
        .iter()
        .find(|spell| spell.reward == RewardId::SpellShield as u8)
        .copied()
        .expect("zero-charge shield remains in its slot during cooldown");
    assert_eq!(spent.charges, 0);
    assert!(spent.ready_at > room.sim().server_time());
    let spent_frame = a
        .last_quest_frame
        .as_deref()
        .expect("cast produced a public quest frame");
    let spent_view = quest_view_from_frame(spent_frame);
    assert!(spent_view
        .spells
        .iter()
        .any(|spell| spell.reward == RewardId::SpellShield as u8 && spell.charges == 0));
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "reward choice remains actionable after the cast",
        |_, a, _| {
            a.board.actions.iter().any(|action| {
                action.quest == barrow_handle && action.action == 8 && action.target == 0
            })
        },
    );
    a.input(
        &mut room,
        WireCommand::QuestInteract {
            handle: barrow_handle,
            action: 8,
            target: 0,
        },
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        8,
        "replacing the spent shield publishes its world drop",
        |_, a, _| {
            quest_row(a, "barrow_run").is_none()
                && a.board
                    .drops
                    .iter()
                    .any(|drop| drop.reward == RewardId::SpellShield as u8 && drop.charges == 0)
        },
    );
    let dropped = a
        .board
        .drops
        .iter()
        .find(|drop| drop.reward == RewardId::SpellShield as u8)
        .copied()
        .unwrap();
    let drop_frame = a
        .last_quest_frame
        .as_deref()
        .expect("drop has public quest frame");
    let drop_view = quest_view_from_frame(drop_frame);
    assert!(drop_view.drops.iter().any(|drop| {
        drop.reward == RewardId::SpellShield as u8
            && drop.charges == 0
            && drop.ready_at == spent.ready_at
    }));

    let warhorn = a
        .board
        .spells
        .iter()
        .find(|spell| spell.reward == RewardId::Warhorn as u8)
        .copied()
        .unwrap();
    let pos = a.pos(&room);
    a.input(
        &mut room,
        WireCommand::Cast {
            slot: warhorn.slot + 1,
            x: pos.x as f32,
            y: pos.y as f32,
            actor: None,
            t: None,
            seq: None,
        },
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "Warhorn use frees its slot",
        |_, a, _| {
            !a.board
                .spells
                .iter()
                .any(|spell| spell.reward == RewardId::Warhorn as u8)
        },
    );
    let pickup: WireQuestAction = a
        .board
        .actions
        .iter()
        .find(|action| action.action == 9 && action.target == dropped.index)
        .expect("the spent shield drop is available to pick up")
        .clone();
    a.input(
        &mut room,
        WireCommand::QuestInteract {
            handle: pickup.quest,
            action: pickup.action,
            target: pickup.target,
        },
    );
    until(
        &mut room,
        &mut a,
        &mut b,
        8,
        "reacquired zero-charge Spell Shield is published",
        |_, a, _| {
            a.board.spells.iter().any(|spell| {
                spell.reward == RewardId::SpellShield as u8
                    && spell.charges == 0
                    && spell.ready_at == spent.ready_at
            }) && !a.board.drops.iter().any(|drop| drop.index == dropped.index)
        },
    );
    let reacquired_frame = a
        .last_quest_frame
        .as_deref()
        .expect("pickup has public quest frame");
    let reacquired_view = quest_view_from_frame(reacquired_frame);
    assert!(reacquired_view.spells.iter().any(|spell| {
        spell.reward == RewardId::SpellShield as u8
            && spell.charges == 0
            && spell.ready_at == spent.ready_at
    }));
    assert!(!reacquired_view
        .drops
        .iter()
        .any(|drop| drop.index == dropped.index));

    for _ in 0..510 {
        wake(&mut room, &mut a, &mut b);
    }
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "reacquired shield recharges on the original cooldown",
        |_, a, _| {
            a.board
                .spells
                .iter()
                .any(|spell| spell.reward == RewardId::SpellShield as u8 && spell.charges == 1)
        },
    );
}
