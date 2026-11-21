//! P-QUEST25-WIRE initial failing oracle: v19 quest commands and owner state survive the canonical codec.
use rh_protocol::*;

#[test]
fn quest_interaction_command_is_strict_and_round_trips() {
    let msg = ClientMsg::Input {
        seq: 9,
        commands: vec![WireCommand::QuestInteract {
            handle: QuestHandle::new(300).unwrap(),
            action: 5,
            target: 48,
        }],
    };
    let bytes = msg.try_encode().unwrap();
    assert_eq!(ClientMsg::decode(&bytes).unwrap(), msg);
    assert_eq!(PROTOCOL_VERSION, 23);
    let bad_fixed_target = ClientMsg::Input {
        seq: 10,
        commands: vec![WireCommand::QuestInteract {
            handle: QuestHandle::new(1).unwrap(),
            action: 1,
            target: 1,
        }],
    };
    assert!(bad_fixed_target.try_encode().is_err());
    assert_eq!(
        ClientMsg::decode(&[2, 1, 0, 0, 0, 1, 0, 19, 1, 1, 1]),
        Err(DecodeError::BadField("quest interaction"))
    );
    assert_eq!(
        ClientMsg::decode(&[2, 1, 0, 0, 0, 1, 0, 19, 1, 5, 81]),
        Err(DecodeError::BadField("quest interaction"))
    );
}

#[test]
fn quest_owner_payload_round_trips_spell_pending_drop_and_action_offer() {
    let handle = QuestHandle::new(7).unwrap();
    let view = WireQuestView {
        rows: vec![WireQuestRow {
            handle,
            def: "q".into(),
            giver: WireQuestPoint { x: 1.0, y: 2.0 },
            state: 1,
            expires_at: 100.0,
            participants: vec![],
            carrier: None,
            carrier_actor: None,
            done_by: None,
            can_accept: false,
            owner_accepted: true,
            owner_progress: 0,
            owner_visited: 0,
            owner_completed: false,
            owner_ready: false,
            name: "Q".into(),
            giver_label: "G".into(),
            brief: "B".into(),
            objective: "O".into(),
            reward: "R".into(),
        }],
        spells: vec![WireQuestSpell {
            slot: 0,
            reward: 1,
            charges: 2,
            ready_at: 25.0,
        }],
        pending: vec![handle],
        drops: vec![WireQuestDrop {
            index: 3,
            reward: 48,
            charges: 1,
            ready_at: 30.0,
            at: WireQuestPoint { x: 4.0, y: 5.0 },
        }],
        actions: vec![WireQuestAction {
            quest: handle,
            action: 2,
            target: 1,
            at: vec![WireQuestPoint { x: 6.0, y: 7.0 }],
            label: "Choose branch".into(),
        }],
        ..WireQuestView::default()
    };
    let mut duplicate_drop_index = view.clone();
    duplicate_drop_index.drops.push(WireQuestDrop {
        index: 3,
        reward: 2,
        charges: 1,
        ready_at: 0.0,
        at: WireQuestPoint { x: 0.0, y: 0.0 },
    });
    assert!(ServerMsg::State {
        tick: 1,
        items: vec![StateItem::Quests(Some(duplicate_drop_index))]
    }
    .try_encode()
    .is_err());
    let frame = ServerMsg::State {
        tick: 1,
        items: vec![StateItem::Quests(Some(view.clone()))],
    }
    .encode();
    let ServerMsg::State { items, .. } = ServerMsg::decode(&frame).unwrap() else {
        panic!("state")
    };
    assert_eq!(items, vec![StateItem::Quests(Some(view))]);
}
