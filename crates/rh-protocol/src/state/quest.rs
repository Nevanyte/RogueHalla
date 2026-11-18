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

#[derive(Clone, Debug, Default, PartialEq)]
pub struct WireQuestView {
    pub unknown_count: u32,
    pub display_unknown_count: u32,
    pub owner_roster: Option<RosterId>,
    pub roster: Vec<WireRosterEntry>,
    pub rows: Vec<WireQuestRow>,
    pub objectives: Vec<WireQuestObjective>,
}

pub(super) fn valid_quest_point(point: WireQuestPoint) -> bool {
    point.x.is_finite()
        && point.y.is_finite()
        && (0.0..=8191.875).contains(&point.x)
        && (0.0..=8191.875).contains(&point.y)
}

impl WireQuestView {
    pub fn check(&self) -> Result<(), EncodeError> {
        if self.unknown_count > 20 || self.display_unknown_count > self.unknown_count {
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
                || row.def.len() > 16
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

    pub(super) fn encoded_len(&self) -> usize {
        varint_len(self.unknown_count)
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
                .sum::<usize>()
    }

    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(self.unknown_count);
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
    }

    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let unknown_count = r.varint()?;
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
        let view = Self {
            unknown_count,
            display_unknown_count,
            owner_roster,
            roster,
            rows,
            objectives,
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
