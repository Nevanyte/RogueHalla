//! Serialized per-player quest inventory, held turn-ins and world reward loot.
use super::awards::Award;
use crate::math::Vec2;
use crate::state::{Phase, PlayerId, World};
use crate::systems::Cx;
use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Owned {
    pub(crate) award: Award,
    pub(crate) charges: u8,
    pub(crate) max_charges: u8,
    pub(crate) ready_at: f64,
}

impl Owned {
    pub(crate) fn new(award: Award, now: f64) -> Self {
        let charges = if award == Award::LanternWard { 2 } else { 1 };
        Self {
            award,
            charges,
            max_charges: charges,
            ready_at: now,
        }
    }

    fn valid(self) -> bool {
        !self.award.is_permanent()
            && self.ready_at.is_finite()
            && self.ready_at >= 0.
            && self.max_charges == Self::new(self.award, 0.).max_charges
            && self.charges <= self.max_charges
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Owner {
    pub(crate) id: PlayerId,
    pub(crate) slots: [Option<Owned>; 2],
    pub(crate) permanent: Vec<Award>,
    #[serde(default)]
    pub(super) chef_bonus: Option<super::permanent::ChefBonus>,
    pub(crate) pending_choices: Vec<u32>,
}
impl Default for Owner {
    fn default() -> Self {
        Self {
            id: PlayerId(0),
            slots: [None, None],
            permanent: Vec::new(),
            chef_bonus: None,
            pending_choices: Vec::new(),
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Loot {
    pub(crate) id: u32,
    pub(crate) owner: PlayerId,
    pub(crate) spell: Owned,
    pub(crate) at: Vec2,
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct RewardState {
    #[serde(default)]
    pub(super) spell_effects: super::effects::EffectState,
    pub(crate) owners: Vec<Owner>,
    pub(crate) loot: Vec<Loot>,
    pub(crate) next_loot: u32,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) struct SpellView {
    pub(crate) award: Award,
    pub(crate) charges: u8,
    pub(crate) ready_at: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) struct LootView {
    pub(crate) id: u32,
    pub(crate) spell: Owned,
    pub(crate) at: Vec2,
}

pub(super) fn state(w: &World) -> Option<&RewardState> {
    w.quests.as_ref()?.rewards.as_ref()
}
pub(super) fn state_mut(w: &mut World) -> Option<&mut RewardState> {
    w.quests.as_mut()?.rewards.as_mut()
}
fn owner_index(rows: &[Owner], id: PlayerId) -> Option<usize> {
    rows.iter().position(|r| r.id == id)
}
pub(super) fn owner(w: &World, id: PlayerId) -> Option<&Owner> {
    state(w)?.owners.iter().find(|r| r.id == id)
}
pub(super) fn owner_mut(w: &mut World, id: PlayerId) -> Option<&mut Owner> {
    let s = state_mut(w)?;
    let i = match owner_index(&s.owners, id) {
        Some(i) => i,
        None => {
            s.owners.push(Owner {
                id,
                ..Owner::default()
            });
            s.owners.len() - 1
        }
    };
    Some(&mut s.owners[i])
}

pub(crate) fn owned_spells(w: &World, id: PlayerId) -> [Option<SpellView>; 2] {
    owner(w, id).map_or([None, None], |o| {
        o.slots.map(|s| {
            s.map(|v| SpellView {
                award: v.award,
                charges: v.charges,
                ready_at: v.ready_at,
            })
        })
    })
}

pub(crate) fn available_loot(w: &World) -> impl Iterator<Item = LootView> + '_ {
    state(w)
        .into_iter()
        .flat_map(|s| s.loot.iter())
        .map(|l| LootView {
            id: l.id,
            spell: l.spell,
            at: l.at,
        })
}

pub(super) fn grant(w: &mut World, id: PlayerId, award: Award) -> bool {
    let now = w.server_time;
    let Some(o) = owner_mut(w, id) else {
        return false;
    };
    if award.is_permanent() {
        if o.permanent.contains(&award) {
            return false;
        }
        o.permanent.push(award);
        return true;
    }
    let Some(slot) = o.slots.iter_mut().find(|s| s.is_none()) else {
        return false;
    };
    *slot = Some(Owned::new(award, now));
    true
}

pub(super) fn create_loot(w: &mut World, owner: PlayerId, award: Award) -> Option<u32> {
    drop_spell(w, owner, Owned::new(award, w.server_time))
}

fn drop_spell(w: &mut World, owner: PlayerId, spell: Owned) -> Option<u32> {
    let at = w.player(owner)?.pos();
    let s = state_mut(w)?;
    s.next_loot = s.next_loot.checked_add(1)?;
    let id = s.next_loot;
    s.loot.push(Loot {
        id,
        owner,
        spell,
        at,
    });
    Some(id)
}

pub(super) fn pay_pending(cx: &mut Cx) {
    if cx.w.phase != Phase::Active {
        return;
    }
    let offer_count = cx.w.quests.as_ref().map_or(0, |q| q.offers.len());
    for seat in 0..cx.w.players.len() {
        let owner = cx.w.players[seat].id;
        for index in 0..offer_count {
            let offer = cx.w.quests.as_ref().expect("board offers").offers[index].id;
            pay_claim(cx, owner, offer);
        }
    }
}

fn pay_claim(cx: &mut Cx, owner_id: PlayerId, offer: u32) {
    use crate::quests::board::attempts;
    let Some(pending) = attempts::pending(cx.w, owner_id, offer) else {
        return;
    };
    if !ensure_owner(cx.w, owner_id) {
        return;
    }
    if owner(cx.w, owner_id).is_some_and(|o| o.pending_choices.contains(&offer)) {
        return;
    }
    let Some(awards) = super::awards::awards_for_offer(cx.w, offer) else {
        return;
    };
    if awards[0].is_permanent() {
        let award = awards[0];
        if !super::permanent::apply(cx, owner_id, award, pending) {
            if award == Award::TheBargain {
                attempts::reopen_bargain(cx.w, owner_id, offer);
            }
            return;
        }
        if attempts::finish(cx.w, owner_id, offer) {
            crate::quests::notices::owner(
                cx.ev,
                owner_id,
                format!("Reward received: {}", award.name()),
            );
        }
        return;
    }
    let free = owner(cx.w, owner_id).map_or(2, |o| o.slots.iter().filter(|s| s.is_none()).count());
    let spill_draught = awards.len() == 2 && free == 1;
    let all_granted = if spill_draught {
        create_loot(cx.w, owner_id, awards[1]).is_some() && grant(cx.w, owner_id, awards[0])
    } else {
        awards.len() <= free && awards.iter().all(|a| grant(cx.w, owner_id, *a))
    };
    if !all_granted {
        owner_mut(cx.w, owner_id)
            .expect("ensured reward owner")
            .pending_choices
            .push(offer);
        return;
    }
    if attempts::finish(cx.w, owner_id, offer) {
        crate::quests::notices::owner(
            cx.ev,
            owner_id,
            format!(
                "Reward received: {}",
                awards
                    .iter()
                    .map(|a| a.name())
                    .collect::<Vec<_>>()
                    .join(" and ")
            ),
        );
    }
}

fn ensure_owner(w: &mut World, id: PlayerId) -> bool {
    owner_mut(w, id).is_some()
}

pub(super) fn resolve_choice(cx: &mut Cx, id: PlayerId, offer: u32, target: u8) {
    if !crate::quests::board::attempts::at_turn_in(cx.k, cx.w, id, offer) {
        return;
    }

    let Some(current) = owner(cx.w, id) else {
        return;
    };
    if !current.pending_choices.contains(&offer) || target > 2 {
        return;
    }
    let Some(awards) = super::awards::awards_for_offer(cx.w, offer) else {
        return;
    };
    let old_slot = current.slots.get(target as usize).copied().flatten();
    let drops = if target == 2 {
        awards.len()
    } else {
        usize::from(old_slot.is_some()) + awards.len() - 1
    };
    if state(cx.w).is_none_or(|r| r.next_loot.checked_add(drops as u32).is_none()) {
        return;
    }
    if let Some(old) = old_slot {
        drop_spell(cx.w, id, old).expect("prechecked drop capacity and owner");
    }
    if target < 2 {
        let now = cx.w.server_time;
        owner_mut(cx.w, id).unwrap().slots[target as usize] = Some(Owned::new(awards[0], now));
    }
    let remaining = if target == 2 {
        &awards[..]
    } else {
        &awards[1..]
    };
    for &award in remaining {
        create_loot(cx.w, id, award).expect("prechecked drop capacity and owner");
    }
    owner_mut(cx.w, id)
        .unwrap()
        .pending_choices
        .retain(|&pending| pending != offer);
    if crate::quests::board::attempts::finish(cx.w, id, offer) {
        crate::quests::notices::owner(cx.ev, id, "Quest reward choice resolved");
    }
}

pub(super) fn pickup(cx: &mut Cx, id: PlayerId, loot_id: u32) {
    let Some(s) = state(cx.w) else { return };
    let Some(i) = s.loot.iter().position(|l| l.id == loot_id) else {
        return;
    };
    let loot = s.loot[i];
    let Some(p) = cx.w.player(id) else { return };
    if p.seat.bot
        || !p.standing()
        || cx.w.phase != crate::Phase::Active
        || crate::status_runtime::action_blocked(&p.statuses, cx.w.server_time)
        || crate::item_view::pickup_reach(
            crate::barriers::body_map(cx.k, &crate::barriers::held(cx.w), cx.w.rt.gates_locked),
            p.pos(),
            loot.at,
        )
        .is_err()
    {
        return;
    }
    if !ensure_owner(cx.w, id) {
        return;
    }
    let Some(slot) = owner_mut(cx.w, id).and_then(|o| o.slots.iter_mut().find(|s| s.is_none()))
    else {
        return;
    };
    *slot = Some(loot.spell);
    state_mut(cx.w).unwrap().loot.remove(i);
}

pub(super) fn has(w: &World, id: PlayerId, award: Award) -> bool {
    owner(w, id).is_some_and(|o| o.permanent.contains(&award))
}
pub(super) fn validate(w: &World) -> Result<(), String> {
    let Some(s) = state(w) else { return Ok(()) };
    for (i, o) in s.owners.iter().enumerate() {
        if o.pending_choices.iter().enumerate().any(|(i, offer)| {
            o.pending_choices[..i].contains(offer)
                || !crate::quests::board::attempts::has_pending_choice(w, o.id, *offer)
        }) || w.player(o.id).is_none()
            || s.owners[..i].iter().any(|p| p.id == o.id)
            || o.slots.iter().flatten().any(|spell| !spell.valid())
            || o.permanent.iter().any(|a| !a.is_permanent())
            || o.chef_bonus.is_some() != o.permanent.contains(&Award::ChefsKiss)
            || o.chef_bonus.is_some_and(|bonus| !bonus.valid())
        {
            return Err("invalid quest reward inventory".into());
        }
    }
    for (i, l) in s.loot.iter().enumerate() {
        if !l.spell.valid()
            || l.id == 0
            || l.id > s.next_loot
            || w.roster.name(l.owner).is_none()
            || !l.at.x.is_finite()
            || !l.at.y.is_finite()
            || s.loot[..i].iter().any(|p| p.id == l.id)
        {
            return Err("invalid quest reward loot".into());
        }
    }
    Ok(())
}
