//! Frame-local references (P-RAID-0): the actor, prop, chest and quest handles valid for this frame only. Rebuilt from
//! every accepted frame, it is the one place a key becomes a handle, at emit time; a key the frame did not deliver
//! resolves to `None` and its command is dropped (never guessed, never a row index, never a stale handle).
use super::acts::{Act, CastAim, MOB_AIM_SLACK};
use super::percept::MobClass;
use super::percept::RaidPercept;
use crate::bots::bot::CommandRefs;
use rh_sim::{PlayerId, PublicCommand};

/// One quest row's handle in this frame, by its definition id.
#[derive(Clone, Debug, PartialEq)]
struct QuestRef {
    def: String,
    handle: rh_sim::quest_view::QuestHandle,
    can_accept: bool,
}

/// The handles of one accepted frame.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Refs {
    /// Rendered actor key -> this frame's actor id (players and npcs the shared percept rendered).
    actors: Vec<(String, PlayerId)>,
    /// Prop key -> this frame's pickup handle.
    props: Vec<(String, u32)>,
    /// Reward chest key -> this frame's chest handle.
    chests: Vec<(String, u32)>,
    quests: Vec<QuestRef>,
    /// The quest interactions this frame offers: (quest handle, action, target).
    actions: Vec<(u32, u8, u8)>,
    /// The dropped quest spells this frame shows: (index, wire reward id).
    drops: Vec<(u8, u8)>,
    /// The merchant kinds whose offer this frame shows, and whether the owner's trade is still unused.
    merchants: Vec<rh_sim::traders::Kind>,
    trade_used: bool,
    /// The owner carries a consumable.
    potions: bool,
    /// The held quest spells this frame shows: (wire slot, wire reward id, charges).
    spells: Vec<(u8, u8, u8)>,
    /// The living mobs this frame shows: (key, position, elite).
    mobs: Vec<(String, rh_sim::Vec2, bool)>,
    /// The owner's position and status bits (a cast aims at them and is refused while silenced, casting or stunned).
    me_at: (f64, f64),
    effects: u16,
}

impl Refs {
    /// The references of `p`, with the actor ids the shared percept rendered for the same frame.
    pub fn build(p: &RaidPercept, ids: Vec<(String, PlayerId)>) -> Self {
        let quests = p.quests.as_ref();
        Self {
            actors: ids,
            props: p
                .facts
                .props
                .iter()
                .map(|r| (r.key.clone(), r.handle.get()))
                .collect(),
            chests: p
                .facts
                .chests
                .iter()
                .map(|c| (c.key.clone(), c.handle.get()))
                .collect(),
            quests: quests.map_or_else(Vec::new, |q| {
                q.rows
                    .iter()
                    .filter_map(|r| {
                        Some(QuestRef {
                            def: r.def.clone(),
                            handle: rh_sim::quest_view::QuestHandle::new(r.handle.get())?,
                            can_accept: r.can_accept,
                        })
                    })
                    .collect()
            }),
            actions: quests.map_or_else(Vec::new, |q| {
                q.actions
                    .iter()
                    .map(|a| (a.quest.get(), a.action, a.target))
                    .collect()
            }),
            drops: quests.map_or_else(Vec::new, |q| {
                q.drops.iter().map(|d| (d.index, d.reward)).collect()
            }),
            merchants: p.trade.as_ref().map_or_else(Vec::new, |t| {
                t.merchants
                    .iter()
                    .map(|m| match m.kind {
                        rh_protocol::WireTradeKind::Barter => rh_sim::traders::Kind::Barter,
                        rh_protocol::WireTradeKind::Stats => rh_sim::traders::Kind::Stats,
                    })
                    .collect()
            }),
            trade_used: p.trade.as_ref().is_none_or(|t| t.trade_used),
            potions: p.me.potions() > 0,
            spells: quests.map_or_else(Vec::new, |q| {
                q.spells
                    .iter()
                    .map(|s| (s.slot, s.reward, s.charges))
                    .collect()
            }),
            mobs: p
                .mobs
                .iter()
                .filter(|m| m.alive)
                .map(|m| (m.key.clone(), m.at, m.class == MobClass::Elite))
                .collect(),
            me_at: (p.me.at.x, p.me.at.y),
            effects: p.me.effects,
        }
    }

    fn quest(&self, def: &str) -> Option<&QuestRef> {
        self.quests.iter().find(|q| q.def == def)
    }

    /// A cast, if this frame still shows `reward` with a charge in wire slot `slot` and the seat may cast it: not silenced
    /// (bit 4), not casting (bit 4096), not stunned (bit 1) unless the spell is Cleanse (16).
    fn cast(&self, slot: u8, reward: u8, aim: &CastAim) -> Option<PublicCommand> {
        const STUNNED: u16 = 1;
        const SILENCED: u16 = 4;
        const CASTING: u16 = 4096;
        const CLEANSE: u8 = 16;
        if !self
            .spells
            .iter()
            .any(|&(s, r, charges)| s == slot && r == reward && charges >= 1)
        {
            return None;
        }
        if self.effects & (SILENCED | CASTING) != 0
            || (self.effects & STUNNED != 0 && reward != CLEANSE)
        {
            return None;
        }
        let key = match slot {
            0 => rh_sim::abilities::Slot::Q,
            1 => rh_sim::abilities::Slot::E,
            _ => return None,
        };
        let (mut x, mut y, mut id) = (self.me_at.0, self.me_at.1, None);
        match aim {
            CastAim::OwnPosition => {}
            CastAim::Point(p) => (x, y) = (p.x, p.y),
            CastAim::Actor(k) => id = Some(self.actor(k)?),
            CastAim::Mob { at, elite } => {
                let key = self
                    .mobs
                    .iter()
                    .filter(|(_, _, e)| e == elite)
                    .map(|(k, p, _)| (rh_sim::math::hypot(p.x - at.x, p.y - at.y), k))
                    .filter(|(d, _)| *d <= MOB_AIM_SLACK)
                    .min_by(|a, b| a.0.total_cmp(&b.0).then_with(|| a.1.cmp(b.1)))?
                    .1;
                id = Some(self.actor(key)?);
            }
        }
        Some(PublicCommand::Cast {
            slot: key,
            x,
            y,
            id,
            t: None,
            seq: None,
        })
    }

    /// The public command for a queued act, if this frame still offers what it names.
    pub fn act(&self, act: &Act) -> Option<PublicCommand> {
        match act {
            Act::QuestAccept { def } => {
                let q = self.quest(def).filter(|q| q.can_accept)?;
                Some(PublicCommand::QuestAccept { handle: q.handle })
            }
            Act::QuestInteract {
                def,
                action,
                target,
            } => {
                let q = self.quest(def)?;
                self.actions
                    .contains(&(q.handle.get(), *action, *target))
                    .then_some(PublicCommand::QuestInteract {
                        handle: q.handle,
                        action: *action,
                        target: *target,
                    })
            }
            Act::QuestPickup { index, reward } => {
                if !self.drops.contains(&(*index, *reward)) {
                    return None;
                }
                // The drop's action handle is not a row's: it is the one frame handle offered with action 9 and this index.
                let (handle, _, _) = self
                    .actions
                    .iter()
                    .copied()
                    .find(|(_, action, target)| *action == 9 && target == index)?;
                Some(PublicCommand::QuestInteract {
                    handle: rh_sim::quest_view::QuestHandle::new(handle)?,
                    action: 9,
                    target: *index,
                })
            }
            Act::UseConsumable => self.potions.then_some(PublicCommand::UseConsumable),
            Act::Trade { merchant, pick } => (!self.trade_used
                && self.merchants.contains(merchant))
            .then_some(PublicCommand::Trade {
                merchant: *merchant,
                pick: *pick,
            }),
            Act::Cast { slot, reward, aim } => self.cast(*slot, *reward, aim),
        }
    }
}

impl CommandRefs for Refs {
    fn actor(&self, id: &str) -> Option<PlayerId> {
        self.actors
            .iter()
            .find(|(key, _)| key == id)
            .map(|(_, real)| *real)
    }
    fn pickup(&self, id: &str) -> Option<u32> {
        self.props.iter().find(|(k, _)| k == id).map(|(_, h)| *h)
    }
    fn chest(&self, id: &str) -> Option<u32> {
        self.chests.iter().find(|(k, _)| k == id).map(|(_, h)| *h)
    }
}
