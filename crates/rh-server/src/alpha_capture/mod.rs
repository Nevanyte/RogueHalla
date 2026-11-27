//! Optional private source-arena capture. Rooms assemble bounded records and hand them to separate local writers.

mod metrics;
mod writer;

pub(crate) use metrics::RoomMetrics;
use metrics::{Metric, MetricSink};
use rh_sim::replay::{Recorder, Replay, ReplayInput, ReplayStart};
use rh_sim::Sim;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;
use writer::{FileJob, Limits, Stream, StreamStats};

use crate::room::{match_id_string, MatchId};

const METRICS_QUEUE: usize = 128;
const REPLAY_QUEUE: usize = 2;
const REPLAY_TICKS: usize = 144_000;
const REPLAY_INPUTS: usize = 250_000;
const METRICS_BYTES: u64 = 4096;
const REPLAY_BYTES: u64 = 64 * 1024 * 1024;

#[derive(Default)]
pub struct Capture {
    metrics: Option<MetricSink>,
    replay: Option<Stream<Replay>>,
    metrics_stats: Arc<StreamStats>,
    replay_stats: Arc<StreamStats>,
}

#[derive(Default)]
pub struct CaptureOptions {
    metrics_dir: Option<PathBuf>,
    metrics_key_file: Option<PathBuf>,
    replay_dir: Option<PathBuf>,
    recipient: Option<String>,
}

impl CaptureOptions {
    pub fn from_args(args: &[String], source_arena: bool) -> Result<Self, &'static str> {
        let mut result = Self::default();
        let mut args = args.iter().peekable();
        while let Some(arg) = args.next() {
            let set = |present| {
                if present {
                    Err("each alpha capture flag may occur once")
                } else {
                    Ok(())
                }
            };
            if !arg.starts_with("--alpha-") {
                continue;
            }
            if !source_arena {
                return Err("alpha capture requires --runtime source-arena");
            }
            let value = args
                .next_if(|s| !s.starts_with('-') && !s.is_empty())
                .ok_or("alpha capture flags need a value")?;
            match arg.as_str() {
                "--alpha-metrics-dir" => {
                    set(result.metrics_dir.is_some())?;
                    result.metrics_dir = Some(value.into());
                }
                "--alpha-metrics-key-file" => {
                    set(result.metrics_key_file.is_some())?;
                    result.metrics_key_file = Some(value.into());
                }
                "--alpha-replay-dir" => {
                    set(result.replay_dir.is_some())?;
                    result.replay_dir = Some(value.into());
                }
                "--alpha-capture-recipient" => {
                    set(result.recipient.is_some())?;
                    result.recipient = Some(value.clone());
                }
                _ => return Err("unknown alpha capture flag"),
            }
        }
        if result.metrics_dir.is_some() != result.metrics_key_file.is_some() {
            return Err("--alpha-metrics-dir and --alpha-metrics-key-file must be set together");
        }
        if (result.metrics_dir.is_some() || result.replay_dir.is_some())
            != result.recipient.is_some()
        {
            return Err("alpha capture streams require --alpha-capture-recipient");
        }
        Ok(result)
    }

    /// Runs before admission. A failed optional writer is reported independently, and the listener still starts.
    pub fn start(self, authenticated: bool) -> Capture {
        let mut capture = Capture::default();
        let Some(recipient) = self.recipient else {
            return capture;
        };
        capture.metrics_stats.configure(self.metrics_dir.is_some());
        capture.replay_stats.configure(self.replay_dir.is_some());
        if let Err(error) = writer::validate_recipient(&recipient) {
            if self.metrics_dir.is_some() {
                capture.metrics_stats.fail_start(Metric::KIND, error);
            }
            if self.replay_dir.is_some() {
                capture.replay_stats.fail_start(Replay::KIND, error);
            }
            return capture;
        }
        if let Some((dir, key_file)) = self.metrics_dir.zip(self.metrics_key_file) {
            let result = if authenticated {
                MetricSink::start(
                    dir,
                    &key_file,
                    recipient.clone(),
                    capture.metrics_stats.clone(),
                )
            } else {
                Err("authenticated profiles required")
            };
            match result {
                Ok(metrics) => capture.metrics = Some(metrics),
                Err(error) => capture.metrics_stats.fail_start(Metric::KIND, error),
            }
        }
        if let Some(dir) = self.replay_dir {
            match Stream::start(
                dir,
                recipient,
                capture.replay_stats.clone(),
                Limits {
                    queue: REPLAY_QUEUE,
                    plaintext_bytes: REPLAY_BYTES,
                    disk_files: 128,
                    disk_bytes: 2 << 30,
                },
            ) {
                Ok(replay) => capture.replay = Some(replay),
                Err(error) => capture.replay_stats.fail_start(Replay::KIND, error),
            }
        }
        capture
    }
}

impl Capture {
    pub(crate) fn room_metrics(&self, id: MatchId, sim: &Sim) -> Option<RoomMetrics> {
        self.metrics
            .clone()
            .map(|sink| RoomMetrics::new(sink, id, sim))
    }

    pub(crate) fn room_replay(&self, id: MatchId, sim: &Sim) -> Option<RoomReplay> {
        self.replay
            .clone()
            .map(|sink| RoomReplay::new(sink, id, sim))
    }

    pub(crate) fn health(&self) -> serde_json::Value {
        serde_json::json!({"metrics": self.metrics_stats.health(), "replays": self.replay_stats.health()})
    }

    pub(crate) async fn flush(&self) {
        let done = async {
            while self.metrics_stats.pending() != 0 || self.replay_stats.pending() != 0 {
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        };
        if tokio::time::timeout(Duration::from_secs(5), done)
            .await
            .is_err()
        {
            eprintln!("[alpha-capture] event=shutdown_budget_exceeded metrics_pending={} replays_pending={}",
                self.metrics_stats.pending(), self.replay_stats.pending());
        }
    }
}

pub(crate) struct RoomReplay {
    sink: Stream<Replay>,
    id: MatchId,
    recorder: Recorder,
    inputs: usize,
}

impl RoomReplay {
    fn new(sink: Stream<Replay>, id: MatchId, sim: &Sim) -> Self {
        let mut recorder = Recorder::new(sim, sim.config().clone(), ReplayStart::Lobby);
        recorder.replay.match_id = Some(match_id_string(id));
        Self {
            sink,
            id,
            recorder,
            inputs: 0,
        }
    }

    pub(crate) fn record(room: &mut Option<Self>, input: ReplayInput) {
        if let Some(capture) = room {
            if capture.inputs == REPLAY_INPUTS {
                capture.sink.drop_limit();
                *room = None;
                return;
            }
            capture.inputs += 1;
            capture.recorder.record(input);
        }
    }

    pub(crate) fn step(room: &mut Option<Self>, sim: &Sim, completed: bool) {
        let Some(capture) = room else {
            return;
        };
        if capture.recorder.replay.hashes.len() == REPLAY_TICKS {
            capture.sink.drop_limit();
            *room = None;
            return;
        }
        capture.recorder.stepped(sim);
        if completed {
            if let Some(capture) = room.take() {
                let name = format!(
                    "replay-{}.json.age",
                    crate::control::room_id_hex(capture.id)
                );
                capture.sink.enqueue(name, capture.recorder.replay);
            }
        }
    }
}

impl FileJob for Replay {
    const KIND: &'static str = "replay";
}

#[cfg(test)]
pub(crate) mod test_probe {
    use super::*;

    pub(crate) struct Probe {
        pub(crate) capture: Capture,
        metrics: std::sync::mpsc::Receiver<(String, Metric)>,
        replays: std::sync::mpsc::Receiver<(String, Replay)>,
    }

    impl Probe {
        pub(crate) fn new(metrics: bool, replay: bool) -> Self {
            let mut capture = Capture::default();
            let (metric_stream, metrics_rx) = Stream::probe(capture.metrics_stats.clone(), 12);
            let (replay_stream, replays) = Stream::probe(capture.replay_stats.clone(), 2);
            capture.metrics = metrics.then(|| MetricSink::probe(metric_stream));
            capture.replay = replay.then_some(replay_stream);
            Self {
                capture,
                metrics: metrics_rx,
                replays,
            }
        }

        pub(crate) fn metrics(&self) -> Vec<(String, serde_json::Value)> {
            self.metrics
                .try_iter()
                .map(|(name, metric)| (name, serde_json::to_value(metric).unwrap()))
                .collect()
        }

        pub(crate) fn replay(&self) -> Option<(String, Replay)> {
            self.replays.try_recv().ok()
        }
    }
}

#[cfg(test)]
mod tests;
