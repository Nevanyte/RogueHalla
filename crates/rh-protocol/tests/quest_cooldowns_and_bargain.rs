//! The Top 25 playtest found valid simulation outputs rejected by the owner-state and input codecs.
use rh_protocol::*;

#[test]
fn a_depleted_slot_and_drop_retain_their_recharge_clock() {
    let mut view = WireQuestView {
        spells: vec![WireQuestSpell {
            slot: 0,
            reward: 9,
            charges: 0,
            ready_at: 25_350.,
        }],
        drops: vec![WireQuestDrop {
            index: 0,
            reward: 15,
            charges: 0,
            ready_at: 53_900.,
            at: WireQuestPoint { x: 10., y: 20. },
        }],
        ..WireQuestView::default()
    };
    let message = ServerMsg::State {
        tick: 13,
        items: vec![StateItem::Quests(Some(view.clone()))],
    };
    assert_eq!(
        ServerMsg::decode(&message.try_encode().unwrap()).unwrap(),
        message
    );
    view.spells[0].ready_at = 0.;
    assert!(view.check().is_err());
    view.spells.clear();
    view.drops[0].ready_at = 0.;
    assert!(view.check().is_err());
}

#[test]
fn all_pairs_of_different_bargain_stats_survive_client_input() {
    for give in 0..9 {
        for receive in (0..9).filter(|&receive| receive != give) {
            let request = ClientMsg::Input {
                seq: 1,
                commands: vec![WireCommand::QuestInteract {
                    handle: QuestHandle::new(1).unwrap(),
                    action: 5,
                    target: 9 * give + receive,
                }],
            };
            assert_eq!(
                ClientMsg::decode(&request.try_encode().unwrap()).unwrap(),
                request
            );
        }
    }
}
