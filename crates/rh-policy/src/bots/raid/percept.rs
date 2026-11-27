//! RaidBot percept (P-RAID-0): what one accepted Frame14 says (C-OBS), joined to the static PlanMap camps, beside the
//! shared hand percept the Humanizer and `Loot` read. Quest givers (sim mobs under camp 0, actor kind 4) go to `givers`
//! only, never `mobs`; a mob's camp is the nearest `PlanCamp` within `CAMP_JOIN_RADIUS` (an actor carries no camp
//! index). An npc's key is new in every frame (its wire id is a per-frame reference): follow a mob by position.
use crate::bots::perception::{npc_key, player_key, LootFacts};
use crate::bots::planning::PlanMap;
use crate::bots::types::Percept;
use crate::delivery::frame::{status as decoded_status, weapon as decoded_weapon};
use crate::delivery::Frame14;
use rh_protocol::state::WireBuildSummary;
use rh_protocol::state::WireWard;
use rh_protocol::{WireArena, WireBuild, WireField, WireQuestView, WireTradeView};
use rh_sim::mapdata::Zone;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::{FactionId, Status, Vec2, WeaponId};

/// A mob farther than this from every camp belongs to none (it wandered or was pulled far).
pub const CAMP_JOIN_RADIUS: f64 = 480.;

/// The owner's own state.
#[derive(Clone, Debug, PartialEq)]
pub struct Me {
    pub at: Vec2,
    pub hp: f64,
    pub max_hp: f64,
    pub alive: bool,
    pub faction: Option<FactionId>,
    pub weapons: [Option<WeaponId>; 2],
    pub slot: u8,
    pub effects: u16,
    /// Server ms the owner's weapon is ready again.
    pub ready_at: f64,
    pub potion_small: u8,
    pub potion_large: u8,
    pub potions_healing: u8,
    pub healing_until: Option<f64>,
}

impl Me {
    /// Hp as a share of max hp (0 when max hp is unknown).
    pub fn hp_share(&self) -> f64 {
        if self.max_hp > 0. {
            self.hp / self.max_hp
        } else {
            0.
        }
    }
    pub fn potions(&self) -> u32 {
        u32::from(self.potion_small)
            + u32::from(self.potion_large)
            + u32::from(self.potions_healing)
    }
}

/// One camp: the static map row joined to its public `WireCamp` row of this frame (same index).
#[derive(Clone, Debug, PartialEq)]
pub struct CampSeen {
    pub index: u32,
    pub at: Vec2,
    pub zone: Zone,
    /// The map's elite spot (-1: the camp has no elite spot).
    pub elite_spot: i32,
    /// The camp's normal mob type (`MobTypeId as u8`).
    pub mob_type: u8,
    /// The visible HUD reward stat, including an off-theme buff.
    pub reward_stat: Option<u8>,
    /// The camp's elite type, when it has one.
    pub elite: Option<MobTypeId>,
    pub alive: u32,
    pub state: u8,
    pub cleared_at: f64,
    pub respawn_at: f64,
    pub ward: Option<WireWard>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MobClass {
    Regional,
    Elite,
    Zombie,
    Dummy,
}

/// One delivered non-player combat actor.
#[derive(Clone, Debug, PartialEq)]
pub struct MobSeen {
    /// The rendered key (`n<id>`) of this frame only: the wire id is a per-frame reference, so the same mob has a new key
    /// next frame. It names the mob within one think (it equals the owner's `OwnerLock::Held` target in the same frame);
    /// `refs.rs` resolves it to this frame's actor. Follow a mob across frames by position.
    pub key: String,
    pub at: Vec2,
    pub hp_share: f64,
    pub alive: bool,
    pub class: MobClass,
    pub mob_type: Option<MobTypeId>,
    pub weapon: Option<WeaponId>,
    pub faction: Option<FactionId>,
    /// The nearest camp within `CAMP_JOIN_RADIUS`: a label by where the mob stands this frame, not the camp it belongs
    /// to (a member chasing away from home reads as no camp or as a neighbour).
    pub camp: Option<u32>,
    /// Its attack cooldown runs or its bow is drawn (`ACTOR_FLAG_ENGAGED`).
    pub engaged: bool,
}

/// One delivered quest giver (actor kind 4): a place to talk, never a target.
#[derive(Clone, Debug, PartialEq)]
pub struct GiverSeen {
    pub at: Vec2,
}

/// One delivered player.
#[derive(Clone, Debug, PartialEq)]
pub struct PlayerSeen {
    pub key: String,
    pub at: Vec2,
    pub hp_share: f64,
    pub alive: bool,
    pub targetable: bool,
    pub faction: Option<FactionId>,
    pub weapon: Option<WeaponId>,
}

/// Everything one accepted frame tells the RaidBot.
#[derive(Clone, Debug)]
pub struct RaidPercept {
    pub now: f64,
    pub tick: u32,
    /// The shared hand percept (`bots::perception`): what the Humanizer and `Loot` read.
    pub base: Percept,
    /// The frame's prop rows and reward chests with the handles THIS frame delivered.
    pub facts: LootFacts,
    pub toasts: Vec<String>,
    /// The owner's rendered key (`p<id>`).
    pub self_key: String,
    pub me: Me,
    /// The raid is on (phase active) and the owner stands.
    pub active: bool,
    pub camps: Vec<CampSeen>,
    pub mobs: Vec<MobSeen>,
    pub givers: Vec<GiverSeen>,
    pub players: Vec<PlayerSeen>,
    /// The quest board (State key 10): `None` for a bot seat or a world without quests.
    pub quests: Option<WireQuestView>,
    pub trade: Option<WireTradeView>,
    pub fields: Vec<WireField>,
    pub build: Option<WireBuild>,
    pub summary: Option<WireBuildSummary>,
    pub arena: Option<WireArena>,
    /// A hit landed on the owner in this frame.
    pub hit_at: Option<f64>,
}

fn class_of(kind: u8, mob_type: Option<MobTypeId>) -> Option<MobClass> {
    match kind {
        1 => Some(
            if mob_type
                .and_then(|t| rh_sim::mob_catalog::mob_type_by_id(t.as_str()))
                .is_some_and(|d| d.elite)
            {
                MobClass::Elite
            } else {
                MobClass::Regional
            },
        ),
        2 => Some(MobClass::Zombie),
        3 => Some(MobClass::Dummy),
        _ => None,
    }
}

/// The nearest camp to `at` within `CAMP_JOIN_RADIUS` (ties by index).
pub fn camp_of(plan: &PlanMap, at: Vec2) -> Option<u32> {
    let mut best: Option<(u32, f64)> = None;
    for (i, c) in plan.camps.iter().enumerate() {
        let d = rh_sim::math::hypot(c.x - at.x, c.y - at.y);
        if d <= CAMP_JOIN_RADIUS && best.is_none_or(|(_, bd)| d < bd) {
            best = Some((i as u32, d));
        }
    }
    best.map(|(i, _)| i)
}

impl RaidPercept {
    /// Joins one accepted frame with the shared hand percept built from the same frame. `None` when the frame has no
    /// owner (unseated).
    pub fn build(
        obs: &Frame14,
        base: Percept,
        facts: LootFacts,
        hit_at: Option<f64>,
        plan: &PlanMap,
    ) -> Option<Self> {
        let owner = obs.owner.as_ref()?;
        let inventory = obs.collections.inventory.as_ref();
        let me = Me {
            at: Vec2::new(f64::from(owner.x), f64::from(owner.y)),
            hp: f64::from(owner.hp),
            max_hp: f64::from(owner.max_hp),
            alive: decoded_status(owner.status) == Status::Alive,
            faction: FactionId::ALL.get(owner.faction as usize).copied(),
            weapons: inventory.map_or([None, None], |i| {
                i.slots.map(|w| w.and_then(decoded_weapon))
            }),
            slot: inventory.map_or(0, |i| i.active_slot),
            effects: owner.effects,
            ready_at: obs
                .owner_combat
                .as_ref()
                .map_or(0., |c| f64::from(c.ready_at)),
            potion_small: inventory.map_or(0, |i| i.potion_small),
            potion_large: inventory.map_or(0, |i| i.potion_large),
            potions_healing: inventory.map_or(0, |i| i.potions_healing),
            healing_until: inventory.and_then(|i| i.healing_until),
        };
        let mut mobs = Vec::new();
        let mut givers = Vec::new();
        let mut players = Vec::new();
        for a in &obs.actors {
            let at = Vec2::new(f64::from(a.x), f64::from(a.y));
            if a.kind == 4 {
                givers.push(GiverSeen { at });
                continue;
            }
            // The shared percept's filter (`perception::Perception::build`): no neutral, no hidden, inside the camera.
            if a.faction == 15
                || a.effects & crate::bots::wish::EFFECT_HIDDEN != 0
                || !crate::bots::view::in_view_at(me.at, at, 0.)
            {
                continue;
            }
            let alive = decoded_status(a.status) == Status::Alive;
            let faction = FactionId::ALL.get(a.faction as usize).copied();
            if a.kind == 0 {
                players.push(PlayerSeen {
                    key: player_key(rh_sim::PlayerId(a.id)),
                    at,
                    hp_share: f64::from(a.hp_pct) / 100.,
                    alive,
                    targetable: a.status == 1 && a.effects & 128 == 0,
                    faction,
                    weapon: decoded_weapon(a.weapon),
                });
                continue;
            }
            let mob_type = a
                .mob_type_code
                .checked_sub(1)
                .and_then(|n| MobTypeId::ALL.get(n as usize).copied());
            let Some(class) = class_of(a.kind, mob_type) else {
                continue;
            };
            mobs.push(MobSeen {
                key: npc_key(rh_sim::PlayerId(a.id)),
                at,
                hp_share: f64::from(a.hp_pct) / 100.,
                alive,
                class,
                mob_type,
                weapon: decoded_weapon(a.weapon),
                faction,
                camp: camp_of(plan, at),
                engaged: a.flags & rh_protocol::ACTOR_FLAG_ENGAGED != 0,
            });
        }
        let camps = plan
            .camps
            .iter()
            .zip(obs.collections.camps.iter())
            .enumerate()
            .map(|(i, (c, w))| CampSeen {
                index: i as u32,
                at: Vec2::new(c.x, c.y),
                zone: c.zone,
                elite_spot: c.elite_spot,
                mob_type: w.mob_type,
                reward_stat: w.reward.map(|r| r.stat),
                elite: w
                    .elite
                    .and_then(|e| MobTypeId::ALL.get(usize::from(e)).copied()),
                alive: w.alive,
                state: w.state,
                cleared_at: w.cleared_at,
                respawn_at: w.respawn_at,
                ward: w.ward,
            })
            .collect();
        let toasts = obs
            .events
            .iter()
            .flat_map(|b| &b.events)
            .filter_map(|e| match e {
                rh_protocol::WireEvent::Toast { text } => Some(text.clone()),
                _ => None,
            })
            .collect();
        let active = obs.phase == 1 && me.alive;
        Some(Self {
            now: obs.source_time,
            tick: obs.tick,
            base,
            facts,
            toasts,
            self_key: player_key(rh_sim::PlayerId(owner.id)),
            me,
            active,
            camps,
            mobs,
            givers,
            players,
            quests: obs.collections.quests.clone(),
            trade: obs.collections.trade.clone(),
            fields: obs.collections.fields.clone(),
            build: obs.collections.build,
            summary: obs.collections.summary.clone(),
            arena: obs.collections.arena,
            hit_at,
        })
    }

    /// The camp row of `index`, if the frame delivered it.
    pub fn camp(&self, index: u32) -> Option<&CampSeen> {
        self.camps.get(index as usize)
    }

    pub fn dist(&self, to: Vec2) -> f64 {
        rh_sim::math::hypot(to.x - self.me.at.x, to.y - self.me.at.y)
    }

    /// The loot/choice executor's view of this frame (`bots::loot::View`).
    pub fn loot_view(
        &self,
        tier: crate::bots::types::TierId,
        persona: crate::bots::types::PersonaId,
        still_since: f64,
    ) -> crate::bots::loot::View<'_> {
        let p = &self.base;
        crate::bots::loot::View {
            now: p.now,
            at: Vec2::new(p.self_view.x, p.self_view.y),
            alive: p.self_view.status == "alive",
            active: p.hud.phase == "active",
            hp: p.self_view.hp,
            max_hp: p.self_view.max_hp,
            effects: p.self_view.effects,
            weapons: p.self_view.weapons,
            slot: p.self_view.slot,
            faction: p.self_view.faction,
            self_key: &self.self_key,
            fighters: &p.fighters,
            mobs: &p.mobs,
            facts: &self.facts,
            gates_locked: p.hud.gates_locked,
            toasts: &self.toasts,
            still_since,
            engaged: matches!(
                p.self_view.owner_lock,
                Some(crate::bots::types::OwnerLock::Held { .. })
            ),
            persona: crate::bots::types::PersonaParams::by_id(persona),
            tier: crate::bots::types::TierParams::by_id(tier),
        }
    }
}
