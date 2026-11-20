//! [`SnapshotTracker`]: a client's delta decoder, and the errors it reports.

use super::*;

/// Why a client refused a delta (what it held did not change). Each is a reason to ask the server for a Full frame.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum DeltaError {
    /// No snapshot held yet (the connection's first snapshot must be Full, and so must the first after a Welcome).
    NoBase,
    /// The delta's base is not the snapshot held.
    BaseMismatch {
        have: u32,
        want: u32,
    },
    /// The delta has a self but the held snapshot has none.
    SelfWithoutBase,
    /// A departed id the held snapshot does not list.
    UnknownDeparted(u32),
    /// An actor both changed and departed.
    DepartedAndChanged(u32),
    /// An actor the held snapshot does not list, carried without its position.
    IncompleteNewActor(u32),
    /// The result would list more than [`MAX_ACTORS`] actors.
    TooManyActors(usize),
    /// A reconstructed kind-4 actor does not carry the contextual Neutral/None tuple.
    InvalidGiverActor(u32),
    InvalidOwnerCombat,
}

impl DeltaError {
    /// A stable name (the golden vectors' `reason`).
    pub fn code(&self) -> &'static str {
        match self {
            DeltaError::NoBase => "no_base",
            DeltaError::BaseMismatch { .. } => "base_mismatch",
            DeltaError::SelfWithoutBase => "self_without_base",
            DeltaError::UnknownDeparted(_) => "unknown_departed",
            DeltaError::DepartedAndChanged(_) => "departed_and_changed",
            DeltaError::IncompleteNewActor(_) => "incomplete_new_actor",
            DeltaError::TooManyActors(_) => "too_many_actors",
            DeltaError::InvalidGiverActor(_) => "giver_actor",
            DeltaError::InvalidOwnerCombat => "owner_combat",
        }
    }
}

impl std::fmt::Display for DeltaError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{self:?}")
    }
}
impl std::error::Error for DeltaError {}

/// Why [`SnapshotTracker::decode`] refused a frame.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum TrackError {
    Decode(DecodeError),
    Delta(DeltaError),
    /// P-TICK-3: a Snapshot or SnapshotDelta reached a tracker made by [`SnapshotTracker::awaiting_welcome`] before any
    /// Welcome: it has no tick rate to read the frame's time with.
    NoWelcome,
}

impl std::fmt::Display for TrackError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{self:?}")
    }
}
impl std::error::Error for TrackError {}

/// A client's snapshot decoder: the last snapshot applied (the base of the next delta). One per connection; a Welcome resets
/// it, so the first snapshot after one must be Full. A refused delta leaves it exactly as it was.
///
/// P-TICK-3: it reads snapshot times at the tick rate of the latest Welcome it accepted (a refused Welcome changes nothing),
/// and at 20 Hz before any Welcome ([`SnapshotTracker::new`]; every `vNN.json` chain starts so) or not at all
/// ([`SnapshotTracker::awaiting_welcome`]).
#[derive(Clone, Debug, Default)]
pub struct SnapshotTracker {
    /// Milliseconds per tick from the latest accepted Welcome; `None` before one.
    tick_ms: Option<u32>,
    /// Refuse snapshots while `tick_ms` is `None` ([`TrackError::NoWelcome`]) instead of reading them at 20 Hz.
    await_welcome: bool,
    tick: Option<u32>,
    server_time: f64,
    training: bool,
    phase: u8,
    ack_input: u32,
    me: Option<WireSelf>,
    actors: Vec<WireActor>,
    scratch: Vec<WireActor>,
    owner_combat: Option<WireOwnerCombat>,
}

impl SnapshotTracker {
    pub fn new() -> Self {
        Self::default()
    }

    /// P-TICK-3: a tracker that refuses every Snapshot and SnapshotDelta ([`TrackError::NoWelcome`]) until it has decoded a
    /// Welcome, and then reads their time at that Welcome's tick rate.
    pub fn awaiting_welcome() -> Self {
        Self {
            await_welcome: true,
            ..Self::default()
        }
    }

    /// The tick rate's milliseconds this tracker reads snapshot times at: the latest accepted Welcome's, else 50 (20 Hz), or
    /// `None` for an [`SnapshotTracker::awaiting_welcome`] tracker that has accepted no Welcome yet.
    pub fn tick_ms(&self) -> Option<u32> {
        match self.tick_ms {
            None if self.await_welcome => None,
            ms => Some(ms.unwrap_or(TICK_MS)),
        }
    }

    /// Forgets the held snapshot (a new connection or a Welcome). The tick rate is the connection's and stays until the next
    /// accepted Welcome.
    pub fn reset(&mut self) {
        self.tick = None;
        self.server_time = 0.0;
        self.training = false;
        self.me = None;
        self.actors.clear();
        self.owner_combat = None;
    }

    /// The tick of the snapshot held.
    pub fn held_tick(&self) -> Option<u32> {
        self.tick
    }

    /// The actors of the snapshot held, ascending by id.
    pub fn actors(&self) -> &[WireActor] {
        &self.actors
    }

    pub fn me(&self) -> Option<&WireSelf> {
        self.me.as_ref()
    }

    /// The held snapshot as the message a Full frame of it would decode to.
    pub fn snapshot(&self) -> Option<ServerMsg> {
        Some(ServerMsg::Snapshot {
            training: self.training,
            tick: self.tick?,
            server_time: self.server_time,
            phase: self.phase,
            ack_input: self.ack_input,
            me: self.me,
            actors: self.actors.clone(),
            owner_combat: self.owner_combat,
        })
    }

    /// Decodes one server frame. A Snapshot is held and returned; a SnapshotDelta is applied and returned as the Snapshot it
    /// resolves to; a Welcome resets the tracker; everything else is returned as decoded.
    ///
    /// Snapshot and SnapshotDelta times are read at this tracker's rate ([`SnapshotTracker::tick_ms`]); before any Welcome an
    /// [`SnapshotTracker::awaiting_welcome`] tracker refuses them with [`TrackError::NoWelcome`]. A Welcome whose rate is not
    /// 20 or 40 is refused by the decoder and changes nothing.
    pub fn decode(&mut self, frame: &[u8]) -> Result<ServerMsg, TrackError> {
        let Some(tick_ms) = self.tick_ms() else {
            if matches!(frame.first(), Some(&T_SNAPSHOT) | Some(&T_SNAPSHOT_DELTA)) {
                return Err(TrackError::NoWelcome);
            }
            let msg = ServerMsg::decode(frame).map_err(TrackError::Decode)?;
            return self.apply(msg).map_err(TrackError::Delta);
        };
        let msg = ServerMsg::decode_with_tick_ms(frame, tick_ms).map_err(TrackError::Decode)?;
        self.apply(msg).map_err(TrackError::Delta)
    }

    /// [`SnapshotTracker::decode`] for a message already decoded. The message's times are taken as decoded (the caller read
    /// them at this tracker's rate). A Welcome resets it and takes its rate when
    /// that is 20 or 40 (a Welcome at another rate never gets here from [`SnapshotTracker::decode`], which refuses it first).
    pub fn apply(&mut self, msg: ServerMsg) -> Result<ServerMsg, DeltaError> {
        match msg {
            ServerMsg::Welcome { tick_rate, .. } => {
                self.reset();
                if let Some(ms) = tick_ms_for_rate(tick_rate) {
                    self.tick_ms = Some(ms);
                }
                Ok(msg)
            }
            ServerMsg::Snapshot {
                tick,
                server_time,
                phase,
                ack_input,
                me,
                actors,
                training,
                owner_combat,
            } => {
                if let Some(id) = actors
                    .iter()
                    .find(|actor| giver_actor_fault(actor).is_some())
                    .map(|actor| actor.id)
                {
                    return Err(DeltaError::InvalidGiverActor(id));
                }
                if owner_combat_fault(owner_combat.as_ref(), me.is_some(), |id| {
                    actors.binary_search_by_key(&id, |a| a.id).is_ok()
                })
                .is_some()
                {
                    return Err(DeltaError::InvalidOwnerCombat);
                }
                self.tick = Some(tick);
                self.server_time = server_time;
                self.training = training;
                self.phase = phase;
                self.ack_input = ack_input;
                self.me = me;
                self.actors.clear();
                self.actors.extend_from_slice(&actors);
                self.owner_combat = owner_combat;
                Ok(ServerMsg::Snapshot {
                    tick,
                    server_time,
                    phase,
                    ack_input,
                    me,
                    actors,
                    training,
                    owner_combat,
                })
            }
            ServerMsg::SnapshotDelta(d) => {
                self.apply_delta(&d)?;
                Ok(self.snapshot().expect("a delta was just applied"))
            }
            other => Ok(other),
        }
    }

    fn apply_delta(&mut self, d: &SnapshotDelta) -> Result<(), DeltaError> {
        match self.tick {
            None => return Err(DeltaError::NoBase),
            Some(t) if t != d.base_tick => {
                return Err(DeltaError::BaseMismatch {
                    have: t,
                    want: d.base_tick,
                })
            }
            Some(_) => {}
        }
        let me = match (&d.me, &self.me) {
            (None, _) => None,
            (Some(s), Some(b)) => Some(s.apply(b)),
            (Some(_), None) => return Err(DeltaError::SelfWithoutBase),
        };
        let mut out = std::mem::take(&mut self.scratch);
        out.clear();
        let r = Self::merge(&self.actors, d, &mut out);
        if let Err(e) = r {
            self.scratch = out;
            return Err(e);
        }
        if let Some(id) = out
            .iter()
            .find(|actor| giver_actor_fault(actor).is_some())
            .map(|actor| actor.id)
        {
            self.scratch = out;
            return Err(DeltaError::InvalidGiverActor(id));
        }
        let owner_combat = match d.owner_combat {
            Some(owner) => owner,
            None if me.is_none() => None,
            None => self.owner_combat,
        };
        if owner_combat_fault(owner_combat.as_ref(), me.is_some(), |id| {
            out.binary_search_by_key(&id, |a| a.id).is_ok()
        })
        .is_some()
        {
            self.scratch = out;
            return Err(DeltaError::InvalidOwnerCombat);
        }
        std::mem::swap(&mut self.actors, &mut out);
        self.scratch = out;
        self.tick = Some(d.tick);
        self.server_time = d.server_time;
        self.training = d.training;
        if let Some(p) = d.phase {
            self.phase = p;
        }
        self.ack_input = d.ack_input;
        self.me = me;
        self.owner_combat = owner_combat;
        Ok(())
    }

    /// `base` with `d`'s changes and departures applied, into `out` (ascending by id).
    fn merge(
        base: &[WireActor],
        d: &SnapshotDelta,
        out: &mut Vec<WireActor>,
    ) -> Result<(), DeltaError> {
        let dep = &d.departed;
        let (mut bi, mut di) = (0usize, 0usize);
        // Copies (or retires) base actors below `limit`; a departed id that no base actor matches is refused.
        let flush =
            |bi: &mut usize, di: &mut usize, limit: Option<u32>, out: &mut Vec<WireActor>| {
                while *bi < base.len() && limit.is_none_or(|l| base[*bi].id < l) {
                    let a = &base[*bi];
                    if *di < dep.len() && dep[*di] < a.id {
                        return Err(DeltaError::UnknownDeparted(dep[*di]));
                    }
                    if *di < dep.len() && dep[*di] == a.id {
                        *di += 1;
                    } else {
                        out.push(*a);
                    }
                    *bi += 1;
                }
                Ok(())
            };
        for ch in &d.changed {
            flush(&mut bi, &mut di, Some(ch.q.id), out)?;
            if bi < base.len() && base[bi].id == ch.q.id {
                if di < dep.len() && dep[di] == ch.q.id {
                    return Err(DeltaError::DepartedAndChanged(ch.q.id));
                }
                out.push(ch.apply(&base[bi]));
                bi += 1;
            } else {
                if ch.mask & AD_NEW != AD_NEW {
                    return Err(DeltaError::IncompleteNewActor(ch.q.id));
                }
                // A new actor starts from the default one: the fields it does not carry are at their defaults.
                let start = QActor {
                    id: ch.q.id,
                    ..QActor::NEW
                };
                out.push(ch.apply(&start.to_wire()));
            }
        }
        flush(&mut bi, &mut di, None, out)?;
        if di < dep.len() {
            return Err(DeltaError::UnknownDeparted(dep[di]));
        }
        if out.len() > MAX_ACTORS {
            return Err(DeltaError::TooManyActors(out.len()));
        }
        Ok(())
    }
}
