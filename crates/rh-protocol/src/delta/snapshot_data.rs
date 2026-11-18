//! [`SnapshotData`]: one viewer's snapshot as the room hands it to a connection.

use super::*;

/// One viewer's snapshot as the room hands it to that viewer's connection (validated: every frame built from it is one every
/// decoder accepts). Immutable once built; the connection's [`ConnEncoder`] keeps the last one it wrote as its base.
///
/// A handle to a heap block (the snapshot is some 80 bytes plus its actor list): it moves from the room to the outbox slot to the
/// writer's base as a pointer, and the block of a snapshot the writer is finished with is handed back to build the next one in
/// ([`ConnEncoder::take_spare`], [`SnapshotData::build_with`]), so a steady stream allocates nothing and copies nothing.
#[derive(Clone, Debug, PartialEq)]
pub struct SnapshotData(Box<SnapshotInner>);

/// What a [`SnapshotData`] holds (its fields are this module's; read them through the accessors).
#[doc(hidden)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SnapshotInner {
    pub(super) tick: u32,
    pub(super) server_time: f64,
    pub(super) phase: u8,
    pub(super) ack_input: u32,
    pub(super) me: Option<WireSelf>,
    pub(super) actors: Vec<QActor>,
    /// Protocol 11: the source's public training fact (head bit 16 on a Full frame, bit 18 on a delta).
    pub(super) training: bool,
    pub(super) owner_combat: Option<WireOwnerCombat>,
    /// [`SnapshotData::min_len`], worked out once.
    pub(super) min_len: u32,
}

impl std::ops::Deref for SnapshotData {
    type Target = SnapshotInner;
    #[inline]
    fn deref(&self) -> &SnapshotInner {
        &self.0
    }
}

impl SnapshotData {
    /// Quantizes and checks a snapshot, refusing (never repairing) anything a decoder would reject: more than [`MAX_ACTORS`]
    /// actors, a phase, status, faction or weapon over its bit width, an `hp_pct` over 100, a position off the wire grid,
    /// ids that do not strictly ascend, or an iterator whose length lies.
    pub fn build<I>(
        tick: u32,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: I,
    ) -> Result<Self, EncodeError>
    where
        I: ExactSizeIterator<Item = WireActor>,
    {
        Self::build_with_server_time(tick, snapshot_server_ms(tick), phase, ack_input, me, actors)
    }

    /// [`SnapshotData::build`] in the block of an earlier snapshot ([`ConnEncoder::take_spare`]; its contents are overwritten),
    /// so a steady stream builds its snapshots without allocating.
    pub fn build_with<I>(
        spare: Option<SnapshotData>,
        tick: u32,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: I,
    ) -> Result<Self, EncodeError>
    where
        I: ExactSizeIterator<Item = WireActor>,
    {
        Self::build_checked_with_server_time(
            spare,
            tick,
            snapshot_server_ms(tick),
            phase,
            ack_input,
            me,
            actors.map(|a| QActor::checked(&a)),
        )
    }

    /// Builds a snapshot with an explicit logical source clock. Ordinary frames should use [`SnapshotData::build`].
    pub fn build_with_server_time<I>(
        tick: u32,
        server_time: f64,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: I,
    ) -> Result<Self, EncodeError>
    where
        I: ExactSizeIterator<Item = WireActor>,
    {
        Self::build_checked_with_server_time(
            None,
            tick,
            server_time,
            phase,
            ack_input,
            me,
            actors.map(|a| QActor::checked(&a)),
        )
    }

    /// [`SnapshotData::build_with`] from actors already checked and quantized one by one ([`QActor::checked`]): a caller that
    /// shows the same actor to several viewers does that work once and passes the result to each. Every refusal of
    /// [`SnapshotData::build_with`] is made here: the list limit, the head, the first faulty actor (an `Err` item, reported in
    /// list order), ids that do not strictly ascend, and an iterator whose length lies.
    pub fn build_checked<I>(
        spare: Option<SnapshotData>,
        tick: u32,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: I,
    ) -> Result<Self, EncodeError>
    where
        I: ExactSizeIterator<Item = Result<QActor, &'static str>>,
    {
        Self::build_checked_with_server_time(
            spare,
            tick,
            snapshot_server_ms(tick),
            phase,
            ack_input,
            me,
            actors,
        )
    }

    /// Reuses a previous snapshot allocation and records this frame's explicit source clock.
    pub fn build_checked_with_server_time<I>(
        spare: Option<SnapshotData>,
        tick: u32,
        server_time: f64,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: I,
    ) -> Result<Self, EncodeError>
    where
        I: ExactSizeIterator<Item = Result<QActor, &'static str>>,
    {
        if !server_time.is_finite() || server_time < 0.0 {
            return Err(EncodeError::BadValue("server time"));
        }
        let declared = actors.len();
        list("actors", declared, MAX_ACTORS)?;
        if phase > MAX_PHASE_CODE
            || me.is_some_and(|s| {
                (s.faction | s.weapon) > MAX_KIT_CODE || s.status > MAX_STATUS_CODE
            })
        {
            if let Some(why) = snapshot_fault(phase, me.as_ref(), &[]) {
                return Err(EncodeError::BadValue(why));
            }
        }
        let mut b = spare.map_or_else(Box::default, |s| s.0);
        let out = &mut b.actors;
        out.clear();
        out.reserve(declared);
        let mut prev: Option<u32> = None;
        for a in actors {
            let q = a.map_err(EncodeError::BadValue)?;
            if prev.is_some_and(|p| q.id <= p) {
                return Err(EncodeError::BadValue("actor order"));
            }
            prev = Some(q.id);
            out.push(q);
        }
        if out.len() != declared {
            return Err(EncodeError::CountMismatch {
                declared,
                written: out.len(),
            });
        }
        b.min_len = (1
            + varint_len(tick)
            + varint_len(ack_input)
            + head_len(head_word(
                phase,
                tick,
                server_time,
                me.as_ref(),
                false,
                false,
            ))
            + (server_time.to_bits() != snapshot_server_ms(tick).to_bits()) as usize * 8
            + varint_len(declared as u32)
            + me.as_ref().map_or(0, |_| MIN_SELF)
            + MIN_ACTOR * declared) as u32;
        b.tick = tick;
        b.server_time = server_time;
        b.phase = phase;
        b.ack_input = ack_input;
        b.me = me;
        b.training = false;
        b.owner_combat = None;
        Ok(SnapshotData(b))
    }

    /// Sets the Full-frame owner-combat group after actor construction, validating references against this exact actor list.
    pub fn set_owner_combat(
        &mut self,
        owner_combat: Option<WireOwnerCombat>,
    ) -> Result<(), EncodeError> {
        if owner_combat_fault(owner_combat.as_ref(), self.me.is_some(), |id| {
            self.actors.binary_search_by_key(&id, |a| a.id).is_ok()
        })
        .is_some()
        {
            return Err(EncodeError::BadValue("owner combat"));
        }
        self.0.owner_combat = owner_combat;
        self.refresh_min_len();
        Ok(())
    }

    fn refresh_min_len(&mut self) {
        let head = head_word(
            self.phase,
            self.tick,
            self.server_time,
            self.me.as_ref(),
            self.training,
            self.owner_combat.is_some(),
        );
        self.0.min_len =
            (1 + varint_len(self.tick)
                + varint_len(self.ack_input)
                + head_len(head)
                + (head & HM_SERVER_TIME != 0) as usize * 8
                + varint_len(self.actors.len() as u32)
                + self.me.as_ref().map_or(0, |_| MIN_SELF)
                + MIN_ACTOR * self.actors.len()
                + self.owner_combat.as_ref().map_or(0, owner_combat_len)) as u32;
    }

    pub fn owner_combat(&self) -> Option<WireOwnerCombat> {
        self.owner_combat
    }

    pub fn tick(&self) -> u32 {
        self.tick
    }
    pub fn server_time(&self) -> f64 {
        self.server_time
    }
    pub fn phase(&self) -> u8 {
        self.phase
    }
    pub fn ack_input(&self) -> u32 {
        self.ack_input
    }
    /// The source's public training fact (protocol 8).
    pub fn training(&self) -> bool {
        self.training
    }
    /// Marks this snapshot as the view of a training world: the wire writes the training head bit (bit 16 on a Full frame,
    /// bit 20 on a delta). Absent for every ordinary match, so those frames stay byte-identical to v7 apart from the
    /// version number.
    pub fn set_training(&mut self, training: bool) {
        if self.training == training {
            return;
        }
        self.0.training = training;
        self.refresh_min_len();
    }
    pub fn me(&self) -> Option<&WireSelf> {
        self.me.as_ref()
    }
    pub fn actors(&self) -> &[QActor] {
        &self.actors
    }
    /// Exact size of this snapshot's Full frame (a pass over the actors: the writer asks only when a delta is not clearly
    /// smaller).
    pub fn full_len(&self) -> usize {
        let head = head_word(
            self.phase,
            self.tick,
            self.server_time,
            self.me.as_ref(),
            self.training,
            self.owner_combat.is_some(),
        );
        let mut n = 1
            + varint_len(self.tick)
            + varint_len(self.ack_input)
            + head_len(head)
            + self.me.as_ref().map_or(0, |s| self_len(s, head))
            + (head & HM_SERVER_TIME != 0) as usize * 8
            + varint_len(self.actors.len() as u32);
        let mut prev = None;
        for q in &self.actors {
            n += q.full_len(prev);
            prev = Some(q.id);
        }
        n += self.owner_combat.as_ref().map_or(0, owner_combat_len);
        n
    }

    /// A capacity to build this snapshot's frame in without growing (what [`ConnEncoder::encode_frame`] reserves): its least Full size plus room for the stack records a delta
    /// is assembled from (they are copied whole, then trimmed).
    pub fn frame_capacity(&self) -> usize {
        self.min_len() + MAX_DELTA_HEAD + MAX_ACTOR_DELTA
    }

    /// A lower bound of [`SnapshotData::full_len`] in constant time: the fewest bytes the head, the self and the actors take.
    #[inline]
    pub fn min_len(&self) -> usize {
        self.min_len as usize
    }

    /// Appends this snapshot as a Full frame: the same bytes `ServerMsg::Snapshot` encodes to for
    /// [`SnapshotData::decoded`].
    pub fn encode_full_into(&self, out: &mut Vec<u8>) -> usize {
        let start = out.len();
        let mut w = Writer::from_vec(std::mem::take(out));
        w.reserve(self.min_len());
        write_snapshot_head(
            &mut w,
            SnapshotHeader {
                tick: self.tick,
                server_time: self.server_time,
                phase: self.phase,
                ack_input: self.ack_input,
                me: self.me.as_ref(),
                training: self.training,
                actors: self.actors.len(),
                owner_combat: self.owner_combat.is_some(),
            },
        );
        let mut prev = None;
        for q in &self.actors {
            write_full_actor(&mut w, q, prev);
            prev = Some(q.id);
        }
        if let Some(owner) = &self.owner_combat {
            write_owner_combat(&mut w, owner);
        }
        *out = w.finish();
        debug_assert_eq!(out.len() - start, self.full_len());
        out.len() - start
    }

    /// What a client decodes this snapshot to (a Full frame or a chain of deltas ending here).
    pub fn decoded(&self) -> ServerMsg {
        ServerMsg::Snapshot {
            tick: self.tick,
            server_time: self.server_time,
            phase: self.phase,
            ack_input: self.ack_input,
            me: self.me,
            actors: self.actors.iter().map(QActor::to_wire).collect(),
            training: self.training,
            owner_combat: self.owner_combat,
        }
    }

    /// Heap and inline bytes this value occupies.
    pub fn memory_bytes(&self) -> usize {
        std::mem::size_of::<Self>()
            + std::mem::size_of::<SnapshotInner>()
            + self.actors.capacity() * std::mem::size_of::<QActor>()
    }
}
