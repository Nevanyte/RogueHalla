//! Optional local capture of the same publicly filtered owner view used by the room.
use super::*;
use serde::Serialize;
use std::{fs::File, io::BufWriter, path::Path};

pub(super) struct Capture {
    observer: crate::ItemObserver,
    pub(super) events: Vec<crate::Event>,
    frames: Vec<Frame>,
    cast: usize,
}

#[derive(Serialize)]
struct Frame {
    phase: String,
    view: crate::Observation,
}

impl Capture {
    pub(super) fn new(seat: PlayerId) -> Self {
        Self {
            observer: crate::ItemObserver::new(seat),
            events: Vec::new(),
            frames: Vec::new(),
            cast: 0,
        }
    }

    pub(super) fn write(self, directory: &Path, arena: &crate::Arena, quest: QuestId, seed: u64) {
        #[derive(Serialize)]
        struct Scenario<'a> {
            id: &'a str,
            name: &'a str,
            reward: &'a str,
            reward_detail: &'a str,
            seed: u64,
            arena: String,
            frames: Vec<Frame>,
        }
        let def = quest.definition();
        let arena_file = format!("arena-{seed}.json");
        let arena_path = directory.join(&arena_file);
        if !arena_path.exists() {
            serde_json::to_writer(BufWriter::new(File::create(arena_path).unwrap()), arena)
                .unwrap();
        }
        let out = File::create(directory.join(format!("quest-{:02}.json", quest as u8))).unwrap();
        serde_json::to_writer(
            BufWriter::new(out),
            &Scenario {
                id: def.id,
                name: def.name,
                reward: if quest == LostBrother {
                    "BlindRage and StillMind"
                } else {
                    rewards::Award::from(def.reward).name()
                },
                reward_detail: def.reward_text,
                seed,
                arena: arena_file,
                frames: self.frames,
            },
        )
        .unwrap();
    }
}

impl Raid {
    pub(super) fn capture_frame(&mut self, phase: impl Into<String>) {
        let Some(capture) = &mut self.capture else {
            return;
        };
        let phase = phase.into();
        let mut view = crate::Observation::default();
        capture
            .observer
            .observe_into(&self.sim, &capture.events, &mut view, None)
            .unwrap();
        capture.events.clear();
        capture.frames.push(Frame { phase, view });
    }

    pub(super) fn capture_ready(&mut self) {
        if self.capture.as_ref().is_some_and(|capture| {
            !capture.frames.iter().any(|frame| frame.phase == "ready")
                && ready(&self.sim.w, self.seat, self.offer)
                && !is_completed(&self.sim.w, self.seat, self.offer)
        }) {
            self.capture_frame("ready");
        }
    }

    pub(super) fn capture_settled(&mut self) {
        if self.capture.as_ref().is_some_and(|capture| {
            !capture.frames.iter().any(|frame| frame.phase == "settled")
                && is_completed(&self.sim.w, self.seat, self.offer)
        }) {
            self.capture_frame("settled");
        }
    }

    pub(super) fn begin_cast_capture(&mut self) {
        if let Some(capture) = &mut self.capture {
            capture.cast += 1;
            self.capture_cast_frame("before");
        }
    }

    pub(super) fn capture_cast_frame(&mut self, stage: &str) {
        if let Some(capture) = &self.capture {
            let phase = match capture.cast {
                0 => return,
                1 => format!("cast_{stage}"),
                n => format!("cast{n}_{stage}"),
            };
            self.capture_frame(phase);
        }
    }

    pub(super) fn discard_cast_capture(&mut self) {
        if let Some(capture) = &mut self.capture {
            capture.frames.truncate(capture.frames.len() - 2);
            capture.cast -= 1;
        }
    }
}
