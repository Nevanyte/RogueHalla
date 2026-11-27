//! The board shared by all seats and this viewer's private journal.
use super::*;
use crate::quests::{
    board::{self, attempts},
    live,
};
use crate::state::Player;

pub(super) fn stage(
    sim: &Sim,
    me: &Player,
    quests: &live::Quests,
    mut next: Committed,
) -> Result<(QuestStage, QuestView), Error> {
    let mut view = QuestView::default();
    next.actionable.clear();
    let planning = sim.phase() == crate::Phase::Planning;
    let preview = planning && sim.config().quest_board.spread();
    let mut seen_zones = [false; 5];
    let mut offers: Vec<_> = quests
        .offers
        .iter()
        .filter(|o| {
            o.published && o.status == Status::Open && !attempts::is_completed(&sim.w, me.id, o.id)
        })
        .filter(|offer| {
            if !preview {
                return true;
            }
            // The private deal's first draw in each zone is the known quest for that zone.
            let Some(quest) = board::quest(usize::from(offer.def)) else {
                return true;
            };
            let seen = &mut seen_zones[usize::from(quest.home().code())];
            let first = !*seen;
            *seen = true;
            if !first {
                view.unknown_count += 1;
            }
            first
        })
        .collect();
    view.display_unknown_count = view.unknown_count;
    offers.sort_by_key(|o| o.def);
    for offer in offers {
        let quest = board::quest(usize::from(offer.def)).ok_or(Error::InvalidState)?;
        let def = quest.definition();
        let handle = match next.quests.get(&offer.id) {
            Some(&handle) => handle,
            None => {
                let handle = QuestHandle(take_id(&mut next.next_quest)?);
                next.quests.insert(offer.id, handle);
                handle
            }
        };
        if !planning {
            next.actionable.insert(offer.id);
        }
        let accepted = attempts::is_accepted(&sim.w, me.id, offer.id);
        if accepted {
            view.objectives.extend(
                (0..8)
                    .filter_map(|i| attempts::objective_for_owner(&sim.w, me.id, offer.id, i))
                    .map(|at| Objective { quest: handle, at }),
            );
            attempts::choices(&sim.k, &sim.w, me.id, offer, handle, &mut view.actions);
            if attempts::pending(&sim.w, me.id, offer.id).is_some() {
                view.pending.push(handle);
                if attempts::at_turn_in(&sim.k, &sim.w, me.id, offer.id) {
                    let slots = board::rewards::owned_spells(&sim.w, me.id);
                    for (slot, equipped) in slots.iter().enumerate() {
                        let label = match equipped {
                            Some(spell) => format!("Drop {}", spell.award.name()),
                            None => format!("Equip in slot {}", slot + 1),
                        };
                        view.actions.push(QuestAction {
                            quest: handle,
                            action: 8,
                            target: slot as u8,
                            at: Vec::new(),
                            label,
                        });
                    }
                    view.actions.push(QuestAction {
                        quest: handle,
                        action: 8,
                        target: 2,
                        at: Vec::new(),
                        label: "Leave the new reward on the ground".into(),
                    });
                }
            }
        }
        let mut carrier = None;
        if let Some((id, _)) = attempts::public_markers(&sim.w, offer.id).next() {
            let roster_id = match next.roster.get(&id) {
                Some(r) => *r,
                None => {
                    let r = RosterId(take_id(&mut next.next_roster)?);
                    next.roster.insert(id, r);
                    r
                }
            };
            if !view.roster.iter().any(|entry| entry.id == roster_id) {
                let label = checked_label(sim.player_name(id).ok_or(Error::InvalidState)?)?;
                view.roster.push(RosterEntry {
                    id: roster_id,
                    label,
                });
            }
            carrier.get_or_insert(roster_id);
        }
        view.rows.push(QuestRow {
            handle,
            def: def.id.to_owned(),
            name: def.name.to_owned(),
            giver_label: def.giver.to_owned(),
            brief: def.objective.to_owned(),
            objective: def.objective.to_owned(),
            reward: def.reward_text.to_owned(),
            giver: offer.at,
            state: if planning {
                RowState::Preview
            } else {
                RowState::Open
            },
            expires_at: attempts::deadline(&sim.w, me.id, offer.id),
            participants: Vec::new(),
            carrier,
            carrier_actor: None,
            done_by: None,
            can_accept: !planning
                && live::accept_eligibility(&sim.k, &sim.w, me.id, Some(offer.id)).is_ok(),
            owner_accepted: accepted,
            owner_progress: u32::from(attempts::progress(&sim.w, me.id, offer.id)),
            owner_visited: 0,
            owner_completed: false,
            owner_ready: attempts::ready(&sim.w, me.id, offer.id),
        });
    }
    for (slot, spell) in board::rewards::owned_spells(&sim.w, me.id)
        .into_iter()
        .enumerate()
    {
        if let Some(spell) = spell {
            view.spells.push(QuestSpell {
                slot: slot as u8,
                reward: spell.award as u8,
                charges: spell.charges,
                ready_at: spell.ready_at,
            });
        }
    }
    let old_drops = std::mem::take(&mut next.drops);
    let wards = crate::vision::WardSight::of_sim(sim);
    let mut loot: Vec<_> = board::rewards::available_loot(&sim.w)
        .filter(|item| {
            crate::vision::point_visible(sim.collision_view(), me, item.at, sim.tick(), wards)
        })
        .collect();
    let position = me.pos();
    let distance = |at: Vec2| crate::math::hypot(at.x - position.x, at.y - position.y);
    loot.sort_by(|a, b| {
        distance(a.at)
            .total_cmp(&distance(b.at))
            .then_with(|| a.id.cmp(&b.id))
    });
    for (i, item) in loot.iter().take(48).enumerate() {
        let index = i as u8;
        view.drops.push(QuestDrop {
            index,
            reward: item.spell.award as u8,
            charges: item.spell.charges,
            ready_at: item.spell.ready_at,
            at: item.at,
        });
        if !me.standing()
            || view.spells.len() == 2
            || crate::item_view::pickup_reach(
                crate::barriers::body_map(
                    &sim.k,
                    &crate::barriers::held(&sim.w),
                    sim.w.rt.gates_locked,
                ),
                position,
                item.at,
            )
            .is_err()
        {
            continue;
        }
        let handle = match old_drops.get(&item.id) {
            Some((handle, _)) => *handle,
            None => QuestHandle(take_id(&mut next.next_quest)?),
        };
        next.drops.insert(item.id, (handle, index));
        view.actions.push(QuestAction {
            quest: handle,
            action: 9,
            target: index,
            at: vec![item.at],
            label: format!("Pick up {}", item.spell.award.name()),
        });
    }
    view.roster.sort_by_key(|r| r.id);
    view.owner_roster = next
        .roster
        .get(&me.id)
        .copied()
        .filter(|id| view.roster.iter().any(|r| r.id == *id));
    next.quests
        .retain(|_, h| view.rows.iter().any(|row| row.handle == *h));
    Ok((QuestStage(next), view))
}

/// These public locations are independent of the recipient's journal and contain no actor identity.
pub(crate) fn public_marks(
    sim: &Sim,
    seated: bool,
    out: &mut Vec<crate::observe::QuestPublicMark>,
) {
    use crate::observe::{QuestPublicMark, QuestPublicMarkKind as Mark};
    use crate::quests::board::catalog::QuestId;
    out.clear();
    if !seated || sim.k.quest_deal.is_none() {
        return;
    }
    let Some(quests) = sim.w.quests.as_ref() else {
        return;
    };
    for offer in &quests.offers {
        let kind = match board::quest(usize::from(offer.def)) {
            Some(QuestId::TheBurningCrown) => Mark::Crown,
            Some(QuestId::SmugglersRun) => Mark::Smuggler,
            Some(QuestId::HoldTheFort) => Mark::Fort,
            Some(QuestId::StayAWhile) => Mark::Meditation,
            Some(QuestId::BountyHunter) => Mark::Bounty,
            _ => continue,
        };
        out.extend(
            attempts::public_markers(&sim.w, offer.id).map(|(_, at)| QuestPublicMark { kind, at }),
        );
    }
}
