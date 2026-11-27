//! Per-seat private metrics from authenticated admission, accepted commands and settled combat.

use super::writer::{FileJob, Limits, Stream, StreamStats};
use super::{METRICS_BYTES, METRICS_QUEUE};
use crate::persist::ProfileKey;
use crate::room::{Admission, MatchId};
use rh_sim::{Event, Phase, PlayerId, PublicCommand, Sim};
use serde::Serialize;
use sha2::{Digest, Sha256};
use std::fs::{self, File};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Clone)]
pub(super) struct MetricSink {
    key: Arc<[u8; 32]>,
    stream: Stream<Metric>,
}

impl MetricSink {
    #[cfg(test)]
    pub(super) fn probe(stream: Stream<Metric>) -> Self {
        Self {
            key: Arc::new([5; 32]),
            stream,
        }
    }

    pub(super) fn start(
        dir: PathBuf,
        key_file: &Path,
        recipient: String,
        stats: Arc<StreamStats>,
    ) -> Result<Self, &'static str> {
        let metadata =
            fs::symlink_metadata(key_file).map_err(|_| "cannot inspect metrics key file")?;
        if !metadata.is_file() || metadata.len() != 32 || !super::writer::private_mode(&metadata) {
            return Err("metrics key must be a private regular file with exactly 32 random bytes");
        }
        let mut file = File::open(key_file).map_err(|_| "cannot read metrics key file")?;
        let mut key = [0; 32];
        file.read_exact(&mut key)
            .map_err(|_| "cannot read metrics key file")?;
        if file
            .read(&mut [0])
            .map_err(|_| "cannot read metrics key file")?
            != 0
        {
            return Err("metrics key must contain exactly 32 bytes");
        }
        let stream = Stream::start(
            dir,
            recipient,
            stats,
            Limits {
                queue: METRICS_QUEUE,
                plaintext_bytes: METRICS_BYTES,
                disk_files: 4096,
                disk_bytes: 32 << 20,
            },
        )?;
        Ok(Self {
            key: Arc::new(key),
            stream,
        })
    }

    fn pseudonym(&self, id: &ProfileKey) -> String {
        let digest = match id {
            ProfileKey::Durable(id) => hmac_sha256(
                &self.key,
                b"rh-alpha-profile-v1/postgres/",
                &id.to_be_bytes(),
            ),
            ProfileKey::Local(name) => {
                hmac_sha256(&self.key, b"rh-alpha-profile-v1/static/", name.as_bytes())
            }
        };
        digest.iter().map(|b| format!("{b:02x}")).collect()
    }
}

// RFC 2104 HMAC over SHA-256 with a fixed 32-byte secret, using the existing SHA-256 implementation.
fn hmac_sha256(key: &[u8; 32], domain: &[u8], data: &[u8]) -> [u8; 32] {
    let mut inner = [0x36u8; 64];
    let mut outer = [0x5cu8; 64];
    for (i, &byte) in key.iter().enumerate() {
        inner[i] ^= byte;
        outer[i] ^= byte;
    }
    let digest = Sha256::new()
        .chain_update(inner)
        .chain_update(domain)
        .chain_update(data)
        .finalize();
    Sha256::new()
        .chain_update(outer)
        .chain_update(digest)
        .finalize()
        .into()
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
struct Commands {
    attempts: u32,
    accepted: u32,
    moves: u32,
    attacks: u32,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Metric {
    schema: &'static str,
    match_id: String,
    player: u32,
    profile_pseudonym: String,
    build: &'static str,
    content_hash: String,
    tick_ms: u32,
    admitted_tick: u32,
    admitted_at_unix_ms: u64,
    started_tick: Option<u32>,
    first_move_tick: Option<u32>,
    first_attack_tick: Option<u32>,
    first_damage_dealt_tick: Option<u32>,
    first_damage_taken_tick: Option<u32>,
    combat_ticks: u32,
    deaths: u32,
    disconnects: u32,
    reconnects: u32,
    commands: Commands,
    exit_tick: u32,
    exit_phase: Phase,
    exit_reason: &'static str,
    #[serde(skip)]
    last_damage_tick: Option<u32>,
}

impl FileJob for Metric {
    const KIND: &'static str = "metrics";
}

pub(crate) struct RoomMetrics {
    sink: MetricSink,
    id: String,
    content_hash: String,
    tick_ms: u32,
    combat_window_ticks: u32,
    rows: Vec<Metric>,
    phase: Phase,
    tick: u32,
}

impl RoomMetrics {
    pub(super) fn new(sink: MetricSink, id: MatchId, sim: &Sim) -> Self {
        Self {
            sink,
            id: crate::control::room_id_hex(id),
            content_hash: format!("{:016x}", rh_sim::content::content_hash()),
            tick_ms: sim.tick_ms(),
            combat_window_ticks: rh_sim::constants::PLAYER_REGEN_QUIET_MS as u32 / sim.tick_ms(),
            rows: Vec::new(),
            phase: sim.phase(),
            tick: sim.tick(),
        }
    }

    pub(crate) fn admit(&mut self, player: PlayerId, profile: &str, admission: Admission) {
        let key = ProfileKey::from_report(profile, admission.profile_id);
        self.rows.push(Metric {
            schema: "rh-alpha-metrics/1",
            match_id: self.id.clone(),
            player: player.0,
            profile_pseudonym: self.sink.pseudonym(&key),
            build: rh_sim::replay::BUILD_ID,
            content_hash: self.content_hash.clone(),
            tick_ms: self.tick_ms,
            admitted_tick: self.tick,
            admitted_at_unix_ms: SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .map_or(0, |t| t.as_millis().min(u128::from(u64::MAX)) as u64),
            started_tick: None,
            first_move_tick: None,
            first_attack_tick: None,
            first_damage_dealt_tick: None,
            first_damage_taken_tick: None,
            combat_ticks: 0,
            deaths: 0,
            disconnects: 0,
            reconnects: 0,
            commands: Commands::default(),
            exit_tick: self.tick,
            exit_phase: self.phase,
            exit_reason: "",
            last_damage_tick: None,
        });
    }

    fn row(&mut self, player: PlayerId) -> Option<&mut Metric> {
        self.rows.iter_mut().find(|row| row.player == player.0)
    }

    pub(crate) fn command(&mut self, player: PlayerId, command: PublicCommand, accepted: bool) {
        let tick = self.tick;
        let active = self.phase == Phase::Active;
        let Some(row) = self.row(player) else {
            return;
        };
        row.commands.attempts = row.commands.attempts.saturating_add(1);
        if !accepted {
            return;
        }
        row.commands.accepted = row.commands.accepted.saturating_add(1);
        if matches!(
            command,
            PublicCommand::Move { .. } | PublicCommand::AttackMove { .. }
        ) {
            row.commands.moves = row.commands.moves.saturating_add(1);
            if active {
                row.first_move_tick.get_or_insert(tick);
            }
        }
        if matches!(
            command,
            PublicCommand::Attack { .. } | PublicCommand::AttackMove { .. }
        ) {
            row.commands.attacks = row.commands.attacks.saturating_add(1);
            if active {
                row.first_attack_tick.get_or_insert(tick);
            }
        }
    }

    pub(crate) fn drop_connection(&mut self, player: PlayerId) {
        if let Some(row) = self.row(player) {
            row.disconnects = row.disconnects.saturating_add(1);
        }
    }

    pub(crate) fn resume(&mut self, player: PlayerId) {
        if let Some(row) = self.row(player) {
            row.reconnects = row.reconnects.saturating_add(1);
        }
    }

    pub(crate) fn step(&mut self, sim: &Sim, events: &[Event]) {
        let active_step = self.phase == Phase::Active;
        self.phase = sim.phase();
        self.tick = sim.tick();
        let tick = self.tick;
        if active_step {
            for hit in sim.landed_hits().iter().filter(|hit| hit.amount > 0.0) {
                if let Some(row) = self.row(hit.victim) {
                    row.first_damage_taken_tick.get_or_insert(tick);
                    row.last_damage_tick = Some(tick);
                }
                if let Some(row) = hit.by.and_then(|id| self.row(id)) {
                    row.first_damage_dealt_tick.get_or_insert(tick);
                    row.last_damage_tick = Some(tick);
                }
            }
            for row in &mut self.rows {
                if row.last_damage_tick.is_some_and(|last| {
                    last == tick
                        || (tick - last < self.combat_window_ticks
                            && sim
                                .player(PlayerId(row.player))
                                .is_some_and(|player| player.standing()))
                }) {
                    row.combat_ticks = row.combat_ticks.saturating_add(1);
                }
            }
        }
        for event in events {
            match event {
                Event::RaidStarted => self
                    .rows
                    .iter_mut()
                    .for_each(|row| row.started_tick = Some(tick)),
                Event::Death { id, .. } => {
                    if let Some(row) = self.row(*id) {
                        row.deaths = row.deaths.saturating_add(1);
                        row.last_damage_tick = None;
                    }
                }
                _ => {}
            }
        }
    }

    fn queue_exit(&self, mut row: Metric, reason: &'static str) {
        row.exit_tick = self.tick;
        row.exit_phase = self.phase;
        row.exit_reason = reason;
        let name = format!("metrics-{}-{}.json.age", self.id, row.player);
        self.sink.stream.enqueue(name, row);
    }

    pub(crate) fn exit(&mut self, player: PlayerId, reason: &'static str) {
        if let Some(i) = self.rows.iter().position(|row| row.player == player.0) {
            let row = self.rows.swap_remove(i);
            self.queue_exit(row, reason);
        }
    }

    pub(crate) fn finish(self, reason: &'static str) {
        // A crashing sim is never inspected. This is the last complete room tick and phase.
        let mut metrics = self;
        for row in std::mem::take(&mut metrics.rows) {
            metrics.queue_exit(row, reason);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pseudonym_uses_standard_hmac_sha256() {
        // RFC 4231 test case 1. A short key with zero padding is identical at HMAC's block boundary.
        let mut key = [0; 32];
        key[..20].fill(0x0b);
        let digest: String = hmac_sha256(&key, b"Hi ", b"There")
            .iter()
            .map(|b| format!("{b:02x}"))
            .collect();
        assert_eq!(
            digest,
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"
        );
    }
}

#[cfg(test)]
mod damage_tests {
    use super::*;
    use rh_sim::input::{validate, RawCommand};
    use rh_sim::{FactionId, Placement, Secrets, Vec2};

    #[test]
    fn settled_damage_and_death_are_measured_on_the_sim_step() {
        let placements = [
            Placement {
                profile: "p1".into(),
                faction: FactionId::Fire,
                bot: false,
                at: Vec2::new(230., 384.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
            Placement {
                profile: "p2".into(),
                faction: FactionId::Fire,
                bot: false,
                at: Vec2::new(270., 384.),
                hp: Some(1.0),
                weapon0: None,
                weapon1: None,
            },
        ];
        let mut sim = Sim::reset_active(
            Default::default(),
            rh_sim::arenas::practice_arena(),
            Secrets::from_seed(1),
            &placements,
        );
        let (stream, _rx) = Stream::probe(Arc::new(StreamStats::default()), 2);
        let mut metrics = RoomMetrics::new(MetricSink::probe(stream), [0; 16], &sim);
        for id in [1, 2] {
            metrics.admit(
                PlayerId(id),
                "unused",
                Admission {
                    gen: 1,
                    profile_id: Some(i64::from(id)),
                },
            );
        }
        sim.submit(
            PlayerId(1),
            validate(RawCommand::Attack { a: 0.0, t: None }).unwrap(),
        )
        .unwrap();
        assert_eq!(
            metrics.row(PlayerId(1)).unwrap().first_damage_dealt_tick,
            None
        );
        let events = sim.step();
        metrics.step(&sim, &events);
        let source = metrics.row(PlayerId(1)).unwrap();
        assert_eq!(source.first_damage_dealt_tick, Some(sim.tick()));
        assert_eq!(source.combat_ticks, 1);
        let victim = metrics.row(PlayerId(2)).unwrap();
        assert_eq!(victim.first_damage_taken_tick, Some(sim.tick()));
        assert_eq!(victim.combat_ticks, 1);
        assert_eq!(victim.deaths, 1);
    }
}
