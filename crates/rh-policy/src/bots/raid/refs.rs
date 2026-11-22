//! Frame-local references (P-RAID-0): the actor, prop, chest and quest handles valid for this frame only. Rebuilt from
//! every accepted frame, it is the one place a key becomes a handle, at emit time; a key the frame did not deliver
//! resolves to `None` and its command is dropped (never guessed, never a row index, never a stale handle).
use super::acts::Act;
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
    /// The merchant kinds whose offer this frame shows, and whether the owner's trade is still unused.
    merchants: Vec<rh_sim::traders::Kind>,
    trade_used: bool,
    /// The owner carries a consumable.
    potions: bool,
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
        }
    }

    fn quest(&self, def: &str) -> Option<&QuestRef> {
        self.quests.iter().find(|q| q.def == def)
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
            Act::UseConsumable => self.potions.then_some(PublicCommand::UseConsumable),
            Act::Trade { merchant, pick } => (!self.trade_used && self.merchants.contains(merchant))
                .then_some(PublicCommand::Trade {
                    merchant: *merchant,
                    pick: *pick,
                }),
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
