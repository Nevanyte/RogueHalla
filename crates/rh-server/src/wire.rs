//! Conversions between simulation types and wire types (rh-protocol stays independent of the sim). Codes are the protocol's
//! (docs/CONTRACTS.md C-NET-3): status 0 lobby, 1 alive, 2 dead, 3 mia; phase 0 lobby, 1 active, 2 ended; outcome 0 won,
//! 1 draw, 2 died, 3 mia; faction and weapon codes are the content tables' order.

use rh_policy::delivery::projection::{
    checked_actor, observation_supported, to_wire, wire_owner_combat, wire_self, Wire,
};
pub use rh_policy::delivery::projection::{checked_phase, checked_snapshot, phase, result, status};
#[cfg(test)]
use rh_policy::delivery::projection::{hp_pct, sat16, wire_actor, wire_point, WIRE_NO_SOURCE};
use rh_protocol::quant::POS_MAX;
#[cfg(test)]
use rh_protocol::WirePoint;
use rh_protocol::{QActor, ServerMsg, SnapshotData, WireCommand, WireEvent, WireFx};
use rh_sim::observe::{ActorView, Observation, SeenEvent};
#[cfg(test)]
use rh_sim::sim::{Phase, Status};
use rh_sim::Class;

/// Public intents only. Trade names its fixed catalog kind; Cast references are resolved through
/// the seat's delivered actor references before ordinary validation and submission.
pub fn command(c: WireCommand) -> rh_sim::PublicCommand {
    use rh_sim::PublicCommand as Public;
    match c {
        WireCommand::Trade { kind, pick } => Public::Trade {
            merchant: match kind {
                rh_protocol::WireTradeKind::Barter => rh_sim::traders::Kind::Barter,
                rh_protocol::WireTradeKind::Stats => rh_sim::traders::Kind::Stats,
            },
            pick: pick as i8,
        },
        WireCommand::QuestAccept { handle } => Public::QuestAccept {
            handle: rh_sim::quest_view::QuestHandle::new(handle.get())
                .expect("nonzero wire quest handle"),
        },
        WireCommand::UseConsumable => Public::UseConsumable,
        // This remains a public reference: Room intake resolves it through the seat ItemObserver.
        WireCommand::HuntTarget { actor } => Public::HuntTarget {
            actor: rh_sim::PlayerId(actor),
        },
        WireCommand::ConsumeCorpse { handle } => Public::ConsumeCorpse {
            handle: rh_sim::PickupHandle::new(handle.get()).expect("nonzero wire handle"),
        },
        WireCommand::Target { actor } => Public::Target {
            actor: rh_sim::PlayerId(actor),
        },
        WireCommand::AttackMove { x, y, seq } => Public::AttackMove {
            x: f64::from(x),
            y: f64::from(y),
            seq,
        },
        WireCommand::Move { x, y, seq } => Public::Move {
            x: x as f64,
            y: y as f64,
            seq,
        },
        WireCommand::Swap { slot } => Public::Swap { slot },
        WireCommand::Pickup { handle } => Public::Pickup {
            handle: rh_sim::PickupHandle::new(handle.get()).expect("checked wire handle"),
        },
        WireCommand::Choose { handle, pick } => Public::Choose {
            handle: rh_sim::reward_chests::RewardChestHandle::from_wire(handle.get())
                .expect("checked reward handle"),
            pick,
        },
        WireCommand::Plan { spawn, boon, ready } => Public::Plan { spawn, boon, ready },
        WireCommand::Stop { seq } => Public::Stop { seq },
        WireCommand::Aim { a } => Public::Aim { a: a as f64 },
        WireCommand::Attack { a, t } => Public::Attack { a: a as f64, t },
        WireCommand::Release { a, t } => Public::Release { a: a as f64, t },
        WireCommand::Cast {
            slot,
            x,
            y,
            actor,
            t,
            seq,
        } => Public::Cast {
            slot: match slot {
                0 => rh_sim::abilities::Slot::Wish,
                1 => rh_sim::abilities::Slot::Q,
                2 => rh_sim::abilities::Slot::E,
                _ => unreachable!("protocol decoder checks Cast slot"),
            },
            x: f64::from(x),
            y: f64::from(y),
            id: actor.map(rh_sim::PlayerId),
            t,
            seq,
        },
        WireCommand::Train {
            command,
            id,
            count,
            options,
            option_count,
        } => Public::Train(rh_sim::input::TrainRaw {
            command,
            id,
            count,
            options,
            option_count,
        }),
    }
}

/// Protocol 14 carries neutral quest givers, private quest/trade/plan state and the training bit. Configuration
/// admission still refuses a training/arena combination: training has no quests, planning or arena lifecycle.
///
/// P-TICK-5 (ADR 0029): a room paces at its sim's step, announces it in every Welcome and sets it on each connection's
/// outbox, so a sim stepping 50 ms (20 Hz) or 25 ms (40 Hz) is hosted; any other step is refused here
/// ([`rh_protocol::checked_tick_ms`]).
pub fn profile_supported(config: &rh_sim::SimConfig) -> Result<(), rh_protocol::EncodeError> {
    rh_protocol::checked_tick_ms(config.tick_ms)?;
    // Measurement only (rh-headless hitbox_compare's A/B): a hosted match never turns mob spacing off.
    if config.mob_spacing != rh_sim::archetype::MobSpacing::Auto {
        return Err(rh_protocol::EncodeError::BadValue(
            "mob spacing off is a measurement-only setting",
        ));
    }
    if config.training && (config.quests || config.lifecycle != rh_sim::ArenaMode::Off) {
        return Err(rh_protocol::EncodeError::BadValue(
            "training cannot host quests or arena lifecycle",
        ));
    }
    Ok(())
}

/// Whether the wire can carry every position of a match on `arena`: the world extent (`w * TILE` by `h * TILE` u) and
/// every spawn must lie within `0 ..= POS_MAX` (8191.875 u, a map of at most 255 tiles a side), the range of the u16 1/8 u
/// position grid of protocol v2. The encoder refuses a position outside it rather than clamp, so a larger map is refused
/// here, at startup, instead of failing every snapshot.
pub fn arena_fits(arena: &rh_sim::Arena) -> Result<(), String> {
    let tile = rh_sim::constants::TILE;
    let limit = POS_MAX as f64;
    let (w, h) = (arena.map.w as f64 * tile, arena.map.h as f64 * tile);
    if w > limit || h > limit {
        return Err(format!(
            "map {}x{} tiles is a world of {w} x {h} u, which does not fit the wire position range 0..={limit} u \
             (protocol v2: u16 at 1/8 u, at most {} tiles a side)",
            arena.map.w,
            arena.map.h,
            (limit / tile) as u32
        ));
    }
    if let Some(p) = arena
        .spawns
        .iter()
        .find(|p| !(0.0..=limit).contains(&p.x) || !(0.0..=limit).contains(&p.y))
    {
        return Err(format!(
            "spawn ({}, {}) is outside the wire position range 0..={limit} u",
            p.x, p.y
        ));
    }
    Ok(())
}

pub fn snapshot(obs: &Observation, ack_input: u32) -> ServerMsg {
    checked_snapshot(obs, ack_input)
        .expect("native observation is unsupported by the wire protocol")
}

/// Whether the protocol-6 actor fields match bit for bit (`0.0` and `-0.0`, or two NaNs, never count as equal).
#[inline]
fn same_view(a: &ActorView, b: &ActorView) -> bool {
    a.id == b.id
        && a.x.to_bits() == b.x.to_bits()
        && a.y.to_bits() == b.y.to_bits()
        && a.facing.to_bits() == b.facing.to_bits()
        && a.hp_frac.to_bits() == b.hp_frac.to_bits()
        && a.barrier_frac.to_bits() == b.barrier_frac.to_bits()
        && a.barrier_ring_step == b.barrier_ring_step
        && a.bounty == b.bounty
        && a.status == b.status
        && a.faction == b.faction
        && a.weapon == b.weapon
        && a.sig == b.sig
        && a.effects == b.effects
        && a.displace_seq == b.displace_seq
        && a.kind == b.kind
        && a.mob_type == b.mob_type
        && a.walk == b.walk
}

/// Highest actor id (exclusive) [`ActorMemo`] remembers; an actor with a higher id is converted every time.
const MEMO_IDS: usize = rh_protocol::MAX_ACTORS;

/// The wire form of each actor last shown, by id. An actor is the same to every viewer who sees it, so its check and
/// quantization is done once per distinct state rather than once per viewer (and not again while it stands still). The key is
/// the whole view, compared bitwise, and the result is a pure function of it: a stale entry cannot be returned.
#[derive(Default)]
struct ActorMemo {
    slots: Vec<Option<(ActorView, Result<QActor, &'static str>)>>,
}

impl ActorMemo {
    #[inline]
    fn get(&mut self, a: &ActorView) -> Result<QActor, &'static str> {
        let i = a.id.0 as usize;
        if i >= MEMO_IDS {
            return checked_actor(a);
        }
        if i >= self.slots.len() {
            self.slots.resize_with(i + 1, || None);
        }
        match &mut self.slots[i] {
            Some((src, q)) if same_view(src, a) => *q,
            slot => {
                let q = checked_actor(a);
                *slot = Some((a.clone(), q));
                q
            }
        }
    }
}

/// One viewer's frames for one wake. Each is one allocation of its exact size.
pub struct ViewFrames {
    /// Reliable events (deaths, bounties, the end, the viewer's own result), if any.
    pub important: Option<Vec<u8>>,
    /// Disposable fx the viewer can see, if any.
    pub fx: Option<Vec<u8>>,
    /// The state snapshot as wire data for the connection's writer to encode (Full or delta); an error when the codec
    /// refuses the observation (never a repaired snapshot).
    pub snapshot: Result<SnapshotData, rh_protocol::EncodeError>,
    /// The first refusal of the Events or Fx frame (that frame is then `None`): the codec never clamps or repairs one.
    pub refused: Option<rh_protocol::EncodeError>,
}

impl ViewFrames {
    fn unsupported(error: rh_protocol::EncodeError) -> Self {
        Self {
            important: None,
            fx: None,
            snapshot: Err(error.clone()),
            refused: Some(error),
        }
    }

    /// Bytes handed to the outbox: the events and fx frames, and the snapshot at a lower bound of the size of its Full frame
    /// ([`SnapshotData::min_len`]; the writer sends a Full frame or a smaller delta and counts the real bytes).
    pub fn bytes(&self) -> usize {
        [&self.important, &self.fx]
            .into_iter()
            .flatten()
            .map(Vec::len)
            .sum::<usize>()
            + self.snapshot.as_ref().map_or(0, SnapshotData::min_len)
    }
}

/// A refusing profile check or a failed native observation offer.
#[derive(Debug)]
pub enum PrepareFramesError {
    Unsupported(rh_protocol::EncodeError),
    Offer(rh_sim::OfferError),
}
impl From<rh_sim::OfferError> for PrepareFramesError {
    fn from(error: rh_sim::OfferError) -> Self {
        Self::Offer(error)
    }
}

/// The shared production and benchmark publish path, retaining intermediate buffers.
#[derive(Default)]
pub struct Publisher {
    obs: Observation,
    actors: ActorMemo,
    events: Vec<WireEvent>,
    fx: Vec<WireFx>,
    /// Line-of-sight verdicts the observations reuse for pairs whose endpoints did not move (derived, exact).
    sight: rh_sim::SightCache,
}

impl Publisher {
    /// Hits and misses of the publisher's line-of-sight memo so far (diagnostics).
    pub fn sight_stats(&self) -> rh_sim::SightStats {
        self.sight.stats()
    }

    /// [`rh_sim::observe_events_into`] through the publisher's line-of-sight memo (same events appended).
    pub fn observe_events_into(
        &mut self,
        sim: &rh_sim::Sim,
        viewer: rh_sim::PlayerId,
        events: &[rh_sim::Event],
        out: &mut Vec<SeenEvent>,
    ) {
        rh_sim::observe_events_into_cached(sim, viewer, events, out, &mut self.sight);
    }

    /// Scratch observation: consume only after successful frame/prepared delivery. On refusal it is unspecified.
    pub fn observation(&self) -> &Observation {
        &self.obs
    }

    /// The frames for `viewer` after a wake.
    ///
    /// * `last_step`: the events of the wake's LAST step; the observation is built from them against the sim as it stands.
    /// * `earlier`: what `viewer` could know of the wake's earlier steps, judged step by step as they ran
    ///   ([`rh_sim::observe_events_into`]); empty on a one-step wake. It is emptied here, and those events come first.
    /// * `all`: every event of the wake, for the viewer's own result (a dedicated path, not an observed event).
    pub fn frames(
        &mut self,
        sim: &rh_sim::Sim,
        viewer: rh_sim::PlayerId,
        ack_input: u32,
        last_step: &[rh_sim::Event],
        earlier: &mut Vec<SeenEvent>,
        all: &[rh_sim::Event],
    ) -> ViewFrames {
        self.frames_in(None, sim, viewer, ack_input, last_step, earlier, all)
    }

    /// [`Publisher::frames`] with the snapshot built in `spare`, a snapshot a connection has finished with
    /// ([`rh_protocol::ConnEncoder::take_spare`], [`crate::outbox::Outbox::push_tick`]).
    #[allow(clippy::too_many_arguments)]
    pub fn frames_in(
        &mut self,
        spare: Option<SnapshotData>,
        sim: &rh_sim::Sim,
        viewer: rh_sim::PlayerId,
        ack_input: u32,
        last_step: &[rh_sim::Event],
        earlier: &mut Vec<SeenEvent>,
        all: &[rh_sim::Event],
    ) -> ViewFrames {
        // Hosting admission is enforced by Room/director/main. Component publishers can project
        // an admitted native view without claiming full quest/giver hosting support.
        if let Err(error) =
            rh_sim::try_observe_into_cached(sim, viewer, last_step, &mut self.obs, &mut self.sight)
        {
            let reason = match error {
                rh_sim::OfferError::UnrepresentablePublicVitals => "public vitals",
                rh_sim::OfferError::UnrepresentableOwnerCombat => "owner combat",
                _ => "observation bounds",
            };
            return ViewFrames::unsupported(rh_protocol::EncodeError::BadValue(reason));
        }
        self.build_observation_frames(spare, viewer, ack_input, earlier, all)
    }

    /// An action-producing observer is seat-owned; the caller commits only after its reliable
    /// state and snapshot have entered the outbox. Event-only catch-up never touches this offer.
    #[allow(clippy::too_many_arguments)]
    pub fn frames_prepared<'a>(
        &mut self,
        observer: &'a mut rh_sim::ItemObserver,
        spare: Option<SnapshotData>,
        sim: &rh_sim::Sim,
        ack_input: u32,
        last_step: &[rh_sim::Event],
        earlier: &mut Vec<SeenEvent>,
        all: &[rh_sim::Event],
    ) -> Result<(ViewFrames, rh_sim::item_view::PreparedOffer<'a>), PrepareFramesError> {
        let viewer = observer.viewer();
        let offer = observer.prepare_with_seen(
            sim,
            last_step,
            earlier,
            &mut self.obs,
            Some(&mut self.sight),
        )?;
        observation_supported(&self.obs).map_err(PrepareFramesError::Unsupported)?;
        earlier.clear();
        Ok((
            self.build_observation_frames(spare, viewer, ack_input, earlier, all),
            offer,
        ))
    }

    fn build_observation_frames(
        &mut self,
        spare: Option<SnapshotData>,
        viewer: rh_sim::PlayerId,
        ack_input: u32,
        earlier: &mut Vec<SeenEvent>,
        all: &[rh_sim::Event],
    ) -> ViewFrames {
        if !earlier.is_empty() {
            earlier.append(&mut self.obs.events);
            std::mem::swap(earlier, &mut self.obs.events);
        }
        if let Err(error) = observation_supported(&self.obs) {
            return ViewFrames::unsupported(error);
        }
        self.events.clear();
        self.fx.clear();
        for e in &self.obs.events {
            match to_wire(e) {
                Wire::Event(w) => {
                    debug_assert_eq!(e.class(), Class::Important);
                    self.events.push(w);
                }
                Wire::Fx(w) => {
                    debug_assert_eq!(e.class(), Class::Fx);
                    self.fx.push(w);
                }
                Wire::Omitted => {}
                Wire::Refused(reason) => {
                    return ViewFrames::unsupported(rh_protocol::EncodeError::BadValue(reason))
                }
            }
        }
        for e in all {
            if let rh_sim::Event::Resolved { id, result: r, .. } = e {
                if *id == viewer {
                    debug_assert_eq!(e.class(), Class::Important);
                    self.events.push(WireEvent::Result(result(r)));
                }
            }
        }
        let tick = self.obs.tick;
        // Both go through the refusing encoder: a frame a decoder would reject (an fx position off the grid, too many
        // entries) is not sent, never clamped (release) nor a panic (debug).
        let mut refused = None;
        let important = if self.events.is_empty() {
            None
        } else {
            let msg = ServerMsg::Events {
                tick,
                events: std::mem::take(&mut self.events),
            };
            let frame = encode_or_refuse(&msg, &mut refused);
            if let ServerMsg::Events { events, .. } = msg {
                self.events = events;
            }
            frame
        };
        let fx = if self.fx.is_empty() {
            None
        } else {
            let msg = ServerMsg::Fx {
                tick,
                fx: std::mem::take(&mut self.fx),
            };
            let frame = encode_or_refuse(&msg, &mut refused);
            if let ServerMsg::Fx { fx, .. } = msg {
                self.fx = fx;
            }
            frame
        };
        let memo = &mut self.actors;
        let training = self.obs.training;
        let snapshot = SnapshotData::build_checked_with_server_time(
            spare,
            tick,
            self.obs.server_time,
            phase(self.obs.phase),
            ack_input,
            self.obs.me.as_ref().map(wire_self),
            self.obs.visible.iter().map(|a| memo.get(a)),
        )
        .and_then(|mut snapshot| {
            snapshot.set_training(training);
            snapshot.set_owner_combat(self.obs.owner_combat.map(wire_owner_combat))?;
            Ok(snapshot)
        });
        ViewFrames {
            important,
            fx,
            snapshot,
            refused,
        }
    }
}

#[cfg(test)]
#[path = "wire_owner_combat_tests.rs"]
mod owner_combat_tests;

/// The frame of `msg`, or `None` with the reason kept in `refused` (the first one wins) when a decoder would reject it:
/// never a clamped frame (release) and never a panic (debug).
fn encode_or_refuse(
    msg: &ServerMsg,
    refused: &mut Option<rh_protocol::EncodeError>,
) -> Option<Vec<u8>> {
    match msg.try_encode() {
        Ok(f) => Some(f),
        Err(e) => {
            refused.get_or_insert(e);
            None
        }
    }
}

#[cfg(test)]
mod tests;
