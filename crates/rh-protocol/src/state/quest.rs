//! The quest log view (State key 11): rows, objectives, participants and their strict validation.

use super::*;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireQuestPoint {
    pub x: f64,
    pub y: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct WireRosterEntry {
    pub id: RosterId,
    pub label: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct WireQuestParticipant {
    pub id: RosterId,
    pub accepted: bool,
    pub progress: u32,
    pub visited: u8,
}

#[derive(Clone, Debug, PartialEq)]
pub struct WireQuestRow {
    pub handle: QuestHandle,
    pub def: String,
    pub giver: WireQuestPoint,
    /// Closed native RowState code: Preview0, Open1, Done2, Expired3.
    pub state: u8,
    pub expires_at: f64,
    pub participants: Vec<WireQuestParticipant>,
    pub carrier: Option<RosterId>,
    /// P-WIRE17-MAP-2: the carrier's public actor id (the id its snapshot actor row carries), present only while the
    /// viewer is delivered that fighter (C-OBS); `None` when the carrier is unseen or there is no carrier. Wire: row flag
    /// 0x10 and a non-zero varint right after the flags byte; only with a carrier.
    pub carrier_actor: Option<u32>,
    pub done_by: Option<String>,
    pub can_accept: bool,
    pub owner_accepted: bool,
    pub owner_progress: u32,
    pub owner_visited: u8,
    pub name: String,
    pub giver_label: String,
    pub brief: String,
    pub objective: String,
    pub reward: String,
    pub owner_completed: bool,
    pub owner_ready: bool,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireQuestObjective {
    pub quest: QuestHandle,
    pub at: WireQuestPoint,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireQuestSpell {
    pub slot: u8,
    pub reward: u8,
    pub charges: u8,
    pub ready_at: f64,
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireQuestDrop {
    pub index: u8,
    pub reward: u8,
    pub charges: u8,
    pub ready_at: f64,
    pub at: WireQuestPoint,
}
#[derive(Clone, Debug, PartialEq)]
pub struct WireQuestAction {
    pub quest: QuestHandle,
    pub action: u8,
    pub target: u8,
    pub at: Vec<WireQuestPoint>,
    pub label: String,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct WireQuestView {
    pub unknown_count: u32,
    pub display_unknown_count: u32,
    pub owner_roster: Option<RosterId>,
    pub roster: Vec<WireRosterEntry>,
    pub rows: Vec<WireQuestRow>,
    pub objectives: Vec<WireQuestObjective>,
    pub spells: Vec<WireQuestSpell>,
    pub pending: Vec<QuestHandle>,
    pub drops: Vec<WireQuestDrop>,
    pub actions: Vec<WireQuestAction>,
}

pub(super) fn valid_quest_point(point: WireQuestPoint) -> bool {
    point.x.is_finite()
        && point.y.is_finite()
        && (0.0..=8191.875).contains(&point.x)
        && (0.0..=8191.875).contains(&point.y)
}

// A reusable spell can be equipped or dropped while its last charge is recharging.
fn valid_spell_clock(charges: u8, ready_at: f64) -> bool {
    ready_at.is_finite() && ready_at >= 0. && (charges > 0 || ready_at > 0.)
}

impl WireQuestView {
    pub fn check(&self) -> Result<(), EncodeError> {
        if self.unknown_count > 25 || self.display_unknown_count > self.unknown_count {
            return Err(EncodeError::BadValue("quest counts"));
        }
        if self.roster.len() > MAX_QUEST_ROSTER {
            return Err(EncodeError::TooMany {
                what: "quest roster",
                n: self.roster.len(),
                max: MAX_QUEST_ROSTER,
            });
        }
        if self.rows.len() > MAX_QUEST_ROWS {
            return Err(EncodeError::TooMany {
                what: "quest rows",
                n: self.rows.len(),
                max: MAX_QUEST_ROWS,
            });
        }
        if self.objectives.len() > MAX_QUEST_OBJECTIVES {
            return Err(EncodeError::TooMany {
                what: "quest objectives",
                n: self.objectives.len(),
                max: MAX_QUEST_OBJECTIVES,
            });
        }
        if self.spells.len() > MAX_QUEST_SPELLS
            || self.pending.len() > MAX_QUEST_PENDING
            || self.drops.len() > MAX_QUEST_DROPS
            || self.actions.len() > MAX_QUEST_ACTIONS
        {
            return Err(EncodeError::BadValue("quest owner data count"));
        }
        if self
            .spells
            .iter()
            .any(|s| s.slot > 1 || s.reward > 48 || !valid_spell_clock(s.charges, s.ready_at))
            || self.spells.windows(2).any(|w| w[0].slot >= w[1].slot)
            || self
                .pending
                .iter()
                .any(|h| !self.rows.iter().any(|r| r.handle == *h))
            || has_duplicates(&self.pending.iter().map(|h| h.get()).collect::<Vec<_>>())
        {
            return Err(EncodeError::BadValue("quest owner spells/pending"));
        }
        let mut drop_ids = Vec::with_capacity(self.drops.len());
        for d in &self.drops {
            if d.reward > 48
                || !valid_spell_clock(d.charges, d.ready_at)
                || !valid_quest_point(d.at)
            {
                return Err(EncodeError::BadValue("quest drop"));
            }
            drop_ids.push(u32::from(d.index));
        }
        if has_duplicates(&drop_ids) {
            return Err(EncodeError::BadValue("quest drop indexes"));
        }
        for a in &self.actions {
            let reference_ok = if a.action == 9 {
                self.drops.iter().any(|d| d.index == a.target)
            } else {
                self.rows.iter().any(|r| r.handle == a.quest)
            };
            if !reference_ok
                || !(1..=9).contains(&a.action)
                || (matches!(a.action, 1 | 4 | 6) && a.target != 0)
                || (a.action == 2 && a.target > 1)
                || (a.action == 5 && a.target > 80)
                || (a.action == 8 && a.target > 2)
                || a.at.len() > MAX_QUEST_ACTION_POINTS
                || a.at.iter().any(|p| !valid_quest_point(*p))
            {
                return Err(EncodeError::BadValue("quest action"));
            }
            str_fits("quest action label", &a.label)?;
        }
        let roster_ids: Vec<u32> = self.roster.iter().map(|entry| entry.id.get()).collect();
        for entry in &self.roster {
            str_fits("quest roster label", &entry.label)?;
        }
        if has_duplicates(&roster_ids) {
            return Err(EncodeError::BadValue("quest roster"));
        }
        if self
            .owner_roster
            .is_some_and(|id| !roster_ids.contains(&id.get()))
        {
            return Err(EncodeError::BadValue("quest references"));
        }
        let mut quest_ids = Vec::with_capacity(self.rows.len());
        for row in &self.rows {
            if row.def.is_empty()
                || row.def.len() > 32
                || row.state > 3
                || !valid_quest_point(row.giver)
                || !row.expires_at.is_finite()
                || row.expires_at < 0.0
                || row.owner_visited & !0x1f != 0
            {
                return Err(EncodeError::BadValue("quest row"));
            }
            if let Some(label) = &row.done_by {
                str_fits("quest done_by", label)?;
            }
            if row
                .carrier_actor
                .is_some_and(|actor| actor == 0 || row.carrier.is_none())
            {
                return Err(EncodeError::BadValue("quest carrier actor"));
            }
            for (what, text) in [
                ("quest name", &row.name),
                ("quest giver label", &row.giver_label),
                ("quest brief", &row.brief),
                ("quest objective text", &row.objective),
                ("quest reward", &row.reward),
            ] {
                str_fits(what, text)?;
            }
            if row.participants.len() > MAX_QUEST_PARTICIPANTS {
                return Err(EncodeError::TooMany {
                    what: "quest participants",
                    n: row.participants.len(),
                    max: MAX_QUEST_PARTICIPANTS,
                });
            }
            let participant_ids: Vec<u32> =
                row.participants.iter().map(|part| part.id.get()).collect();
            if has_duplicates(&participant_ids)
                || row
                    .participants
                    .iter()
                    .any(|part| part.visited & !0x1f != 0 || !roster_ids.contains(&part.id.get()))
                || row
                    .carrier
                    .is_some_and(|id| !roster_ids.contains(&id.get()))
            {
                return Err(EncodeError::BadValue("quest references"));
            }
            quest_ids.push(row.handle.get());
        }
        if has_duplicates(&quest_ids) {
            return Err(EncodeError::BadValue("quest handles"));
        }
        let mut per_quest = vec![0u8; self.rows.len()];
        for objective in &self.objectives {
            if !valid_quest_point(objective.at) {
                return Err(EncodeError::BadValue("quest objective"));
            }
            let Some(index) = quest_ids.iter().position(|&id| id == objective.quest.get()) else {
                return Err(EncodeError::BadValue("quest references"));
            };
            per_quest[index] = per_quest[index].saturating_add(1);
            if usize::from(per_quest[index]) > MAX_QUEST_OBJECTIVES_PER_ROW {
                return Err(EncodeError::TooMany {
                    what: "quest objectives per row",
                    n: usize::from(per_quest[index]),
                    max: MAX_QUEST_OBJECTIVES_PER_ROW,
                });
            }
        }
        if 1 + self.encoded_len() > MAX_QUEST_VIEW_BYTES {
            return Err(EncodeError::TooLarge {
                len: 1 + self.encoded_len(),
                max: MAX_QUEST_VIEW_BYTES,
            });
        }
        Ok(())
    }

    // The historical unknown count occupies bits 0..4. Bit 6 introduces the optional v19 owner groups.
    // A legacy or empty board retains its original bytes, including the local v14 scene experiments.
    const OWNER_GROUPS: u32 = 0x40;
    fn has_owner_groups(&self) -> bool {
        !(self.spells.is_empty()
            && self.pending.is_empty()
            && self.drops.is_empty()
            && self.actions.is_empty())
    }

    pub(super) fn encoded_len(&self) -> usize {
        let base = varint_len(self.unknown_count)
            + varint_len(self.display_unknown_count)
            + varint_len(self.owner_roster.map_or(0, RosterId::get))
            + varint_len(self.roster.len() as u32)
            + self
                .roster
                .iter()
                .map(|entry| varint_len(entry.id.get()) + str_len(&entry.label))
                .sum::<usize>()
            + varint_len(self.rows.len() as u32)
            + self
                .rows
                .iter()
                .map(WireQuestRow::encoded_len)
                .sum::<usize>()
            + varint_len(self.objectives.len() as u32)
            + self
                .objectives
                .iter()
                .map(WireQuestObjective::encoded_len)
                .sum::<usize>();
        if !self.has_owner_groups() {
            return base;
        }
        base + varint_len(self.spells.len() as u32)
            + self.spells.len() * 11
            + varint_len(self.pending.len() as u32)
            + self
                .pending
                .iter()
                .map(|h| varint_len(h.get()))
                .sum::<usize>()
            + varint_len(self.drops.len() as u32)
            + self.drops.len() * 27
            + varint_len(self.actions.len() as u32)
            + self
                .actions
                .iter()
                .map(|a| {
                    varint_len(a.quest.get())
                        + 2
                        + varint_len(a.at.len() as u32)
                        + a.at.len() * 16
                        + str_len(&a.label)
                })
                .sum::<usize>()
    }

    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(
            self.unknown_count
                | if self.has_owner_groups() {
                    Self::OWNER_GROUPS
                } else {
                    0
                },
        );
        w.varint(self.display_unknown_count);
        w.varint(self.owner_roster.map_or(0, RosterId::get));
        w.varint(self.roster.len() as u32);
        for entry in &self.roster {
            w.varint(entry.id.get());
            w.str(&entry.label);
        }
        w.varint(self.rows.len() as u32);
        for row in &self.rows {
            row.write(w);
        }
        w.varint(self.objectives.len() as u32);
        for objective in &self.objectives {
            objective.write(w);
        }
        if !self.has_owner_groups() {
            return;
        }
        w.varint(self.spells.len() as u32);
        for s in &self.spells {
            w.u8(s.slot);
            w.u8(s.reward);
            w.u8(s.charges);
            w.f64(s.ready_at);
        }
        w.varint(self.pending.len() as u32);
        for h in &self.pending {
            w.varint(h.get());
        }
        w.varint(self.drops.len() as u32);
        for d in &self.drops {
            w.u8(d.index);
            w.u8(d.reward);
            w.u8(d.charges);
            w.f64(d.ready_at);
            w.f64(d.at.x);
            w.f64(d.at.y);
        }
        w.varint(self.actions.len() as u32);
        for a in &self.actions {
            w.varint(a.quest.get());
            w.u8(a.action);
            w.u8(a.target);
            w.varint(a.at.len() as u32);
            for p in &a.at {
                w.f64(p.x);
                w.f64(p.y);
            }
            w.str(&a.label);
        }
    }

    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let header = r.varint()?;
        let unknown_count = header & !Self::OWNER_GROUPS;
        let display_unknown_count = r.varint()?;
        let owner_roster = roster_id_or_none(r.varint()?)?;
        let roster_len = r.varint()? as usize;
        if roster_len > MAX_QUEST_ROSTER {
            return Err(DecodeError::TooMany(roster_len));
        }
        let mut roster = Vec::with_capacity(roster_len);
        for _ in 0..roster_len {
            roster.push(WireRosterEntry {
                id: roster_id(r.varint()?)?,
                label: r.str()?,
            });
        }
        let rows_len = r.varint()? as usize;
        if rows_len > MAX_QUEST_ROWS {
            return Err(DecodeError::TooMany(rows_len));
        }
        let mut rows = Vec::with_capacity(rows_len);
        for _ in 0..rows_len {
            rows.push(WireQuestRow::read(r)?);
        }
        let objectives_len = r.varint()? as usize;
        if objectives_len > MAX_QUEST_OBJECTIVES {
            return Err(DecodeError::TooMany(objectives_len));
        }
        let mut objectives = Vec::with_capacity(objectives_len);
        for _ in 0..objectives_len {
            objectives.push(WireQuestObjective::read(r)?);
        }
        if header & Self::OWNER_GROUPS == 0 {
            let view = Self {
                unknown_count,
                display_unknown_count,
                owner_roster,
                roster,
                rows,
                objectives,
                ..Default::default()
            };
            view.check().map_err(quest_decode_error)?;
            return Ok(view);
        }
        let spells_len = r.varint()? as usize;
        if spells_len > MAX_QUEST_SPELLS {
            return Err(DecodeError::TooMany(spells_len));
        }
        let mut spells = Vec::with_capacity(spells_len);
        for _ in 0..spells_len {
            spells.push(WireQuestSpell {
                slot: r.u8()?,
                reward: r.u8()?,
                charges: r.u8()?,
                ready_at: r.f64()?,
            });
        }
        let pending_len = r.varint()? as usize;
        if pending_len > MAX_QUEST_PENDING {
            return Err(DecodeError::TooMany(pending_len));
        }
        let mut pending = Vec::with_capacity(pending_len);
        for _ in 0..pending_len {
            pending.push(quest_handle(r.varint()?)?);
        }
        let drops_len = r.varint()? as usize;
        if drops_len > MAX_QUEST_DROPS {
            return Err(DecodeError::TooMany(drops_len));
        }
        let mut drops = Vec::with_capacity(drops_len);
        for _ in 0..drops_len {
            drops.push(WireQuestDrop {
                index: r.u8()?,
                reward: r.u8()?,
                charges: r.u8()?,
                ready_at: r.f64()?,
                at: WireQuestPoint {
                    x: r.f64()?,
                    y: r.f64()?,
                },
            });
        }
        let actions_len = r.varint()? as usize;
        if actions_len > MAX_QUEST_ACTIONS {
            return Err(DecodeError::TooMany(actions_len));
        }
        let mut actions = Vec::with_capacity(actions_len);
        for _ in 0..actions_len {
            let quest = quest_handle(r.varint()?)?;
            let action = r.u8()?;
            let target = r.u8()?;
            let n = r.varint()? as usize;
            if n > MAX_QUEST_ACTION_POINTS {
                return Err(DecodeError::TooMany(n));
            }
            let mut at = Vec::with_capacity(n);
            for _ in 0..n {
                at.push(WireQuestPoint {
                    x: r.f64()?,
                    y: r.f64()?,
                });
            }
            actions.push(WireQuestAction {
                quest,
                action,
                target,
                at,
                label: r.str()?,
            });
        }
        let view = Self {
            unknown_count,
            display_unknown_count,
            owner_roster,
            roster,
            rows,
            objectives,
            spells,
            pending,
            drops,
            actions,
        };
        view.check().map_err(quest_decode_error)?;
        Ok(view)
    }
}

impl WireQuestRow {
    pub(super) fn encoded_len(&self) -> usize {
        varint_len(self.handle.get())
            + str_len(&self.def)
            + 16
            + 1
            + 8
            + varint_len(self.participants.len() as u32)
            + self
                .participants
                .iter()
                .map(|part| varint_len(part.id.get()) + 1 + varint_len(part.progress) + 1)
                .sum::<usize>()
            + varint_len(self.carrier.map_or(0, RosterId::get))
            + 1
            + self.done_by.as_deref().map_or(0, str_len)
            + 1
            + self.carrier_actor.map_or(0, varint_len)
            + varint_len(self.owner_progress)
            + 1
            + str_len(&self.name)
            + str_len(&self.giver_label)
            + str_len(&self.brief)
            + str_len(&self.objective)
            + str_len(&self.reward)
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(self.handle.get());
        w.str(&self.def);
        w.f64(self.giver.x);
        w.f64(self.giver.y);
        w.u8(self.state);
        w.f64(self.expires_at);
        w.varint(self.participants.len() as u32);
        for part in &self.participants {
            w.varint(part.id.get());
            w.u8(u8::from(part.accepted));
            w.varint(part.progress);
            w.u8(part.visited);
        }
        w.varint(self.carrier.map_or(0, RosterId::get));
        w.opt(self.done_by.as_ref(), |w, label| w.str(label));
        w.u8(u8::from(self.can_accept)
            | (u8::from(self.owner_accepted) << 1)
            | (u8::from(self.owner_completed) << 2)
            | (u8::from(self.owner_ready) << 3)
            | (u8::from(self.carrier_actor.is_some()) << 4));
        if let Some(actor) = self.carrier_actor {
            w.varint(actor);
        }
        w.varint(self.owner_progress);
        w.u8(self.owner_visited);
        w.str(&self.name);
        w.str(&self.giver_label);
        w.str(&self.brief);
        w.str(&self.objective);
        w.str(&self.reward);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let handle = quest_handle(r.varint()?)?;
        let def = r.str()?;
        let giver = WireQuestPoint {
            x: r.f64()?,
            y: r.f64()?,
        };
        let state = r.u8()?;
        if state > 3 {
            return Err(DecodeError::BadField("quest status"));
        }
        let expires_at = r.f64()?;
        let count = r.varint()? as usize;
        if count > MAX_QUEST_PARTICIPANTS {
            return Err(DecodeError::TooMany(count));
        }
        let mut participants = Vec::with_capacity(count);
        for _ in 0..count {
            participants.push(WireQuestParticipant {
                id: roster_id(r.varint()?)?,
                accepted: quest_bool(r)?,
                progress: r.varint()?,
                visited: r.u8()?,
            });
        }
        let carrier = roster_id_or_none(r.varint()?)?;
        let done_by = if quest_bool(r)? { Some(r.str()?) } else { None };
        let flags = r.u8()?;
        // 0x10: the carrier's actor id follows; it needs a carrier (else the flag is as unknown as 0x20..0x80).
        if flags & !0x1f != 0 || (flags & 0x10 != 0 && carrier.is_none()) {
            return Err(DecodeError::BadField("quest flags"));
        }
        let carrier_actor = if flags & 0x10 != 0 {
            match r.varint()? {
                0 => return Err(DecodeError::BadField("quest carrier actor")),
                actor => Some(actor),
            }
        } else {
            None
        };
        let owner_progress = r.varint()?;
        let owner_visited = r.u8()?;
        Ok(Self {
            handle,
            def,
            giver,
            state,
            expires_at,
            participants,
            carrier,
            carrier_actor,
            done_by,
            can_accept: flags & 1 != 0,
            owner_accepted: flags & 2 != 0,
            owner_completed: flags & 4 != 0,
            owner_ready: flags & 8 != 0,
            owner_progress,
            owner_visited,
            name: r.str()?,
            giver_label: r.str()?,
            brief: r.str()?,
            objective: r.str()?,
            reward: r.str()?,
        })
    }
}

impl WireQuestObjective {
    pub(super) fn encoded_len(&self) -> usize {
        varint_len(self.quest.get()) + 16
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(self.quest.get());
        w.f64(self.at.x);
        w.f64(self.at.y);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        Ok(Self {
            quest: quest_handle(r.varint()?)?,
            at: WireQuestPoint {
                x: r.f64()?,
                y: r.f64()?,
            },
        })
    }
}

pub(super) fn quest_handle(value: u32) -> Result<QuestHandle, DecodeError> {
    QuestHandle::new(value).ok_or(DecodeError::BadField("quest handle"))
}
pub(super) fn roster_id(value: u32) -> Result<RosterId, DecodeError> {
    RosterId::new(value).ok_or(DecodeError::BadField("quest roster id"))
}
pub(super) fn roster_id_or_none(value: u32) -> Result<Option<RosterId>, DecodeError> {
    if value == 0 {
        Ok(None)
    } else {
        roster_id(value).map(Some)
    }
}
pub(super) fn quest_bool(r: &mut Reader<'_>) -> Result<bool, DecodeError> {
    match r.u8()? {
        0 => Ok(false),
        1 => Ok(true),
        _ => Err(DecodeError::BadField("quest flag")),
    }
}
pub(super) fn quest_decode_error(error: EncodeError) -> DecodeError {
    match error {
        EncodeError::TooMany { n, .. } => DecodeError::TooMany(n),
        EncodeError::TooLarge { len, .. } => DecodeError::TooLarge(len),
        EncodeError::BadValue(name) => DecodeError::BadField(name),
        _ => DecodeError::BadField("quest"),
    }
}
pub(super) fn has_duplicates(values: &[u32]) -> bool {
    values
        .iter()
        .enumerate()
        .any(|(i, value)| values[..i].contains(value))
}

/// Validate a candidate's Props and Quest collection together. Stateful publishers call this after retained and replaced
/// collections have been reconstructed; a stateless State patch may carry either key alone.
pub fn validate_quest_props(
    props: &WireProps,
    quests: Option<&WireQuestView>,
) -> Result<(), EncodeError> {
    props.check()?;
    if let Some(view) = quests {
        view.check()?;
    }
    for row in &props.rows {
        if let WirePropKind::QuestItem { quest, .. } = &row.kind {
            if !quests.is_some_and(|view| view.rows.iter().any(|q| q.handle == *quest)) {
                return Err(EncodeError::BadValue("quest prop reference"));
            }
        }
    }
    Ok(())
}
