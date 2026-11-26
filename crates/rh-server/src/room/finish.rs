//! The end of a match: kept and reported results, closing, the final report, stats and a crash report.

use super::*;

impl Room {
    /// Keeps each human's own resolved result on its seat (see [`HumanSeat::result`]).
    pub(super) fn keep_results(&mut self, events: &[Event]) {
        if self.training {
            return;
        }
        for e in events {
            if let Event::Resolved {
                id,
                result,
                bot: false,
            } = e
            {
                if let Some(h) = self.humans.iter_mut().find(|h| h.player == *id) {
                    h.result = Some(wire::result(result));
                }
            }
        }
    }

    /// Every resolved result goes to the director (durable storage), with the fence it was earned under.
    pub(super) fn report_results(&mut self, events: &[Event]) {
        // A training room never settles or banks a run: no result reaches the director (and so no durable match).
        if self.training {
            return;
        }
        for e in events {
            if let Event::Resolved { id, result, bot } = e {
                let (fence, admission) = if *bot {
                    (None, None)
                } else {
                    let Some(seat) = self
                        .fences
                        .iter()
                        .find(|seat| seat.player == *id && seat.fence.is_some())
                    else {
                        continue;
                    };
                    (seat.fence, seat.admission)
                };
                let bot_spec = if *bot {
                    self.bot_specs
                        .iter()
                        .find(|(player, _)| player == id)
                        .map(|(_, spec)| spec.clone())
                } else {
                    None
                };
                if let Some(spec) = &bot_spec {
                    if !self.reported_bots.insert(spec.number) {
                        continue;
                    }
                }
                // Source BotManager.onResult: the seat's `runner.end(result)` summary, asked once.
                let bot_choices = if *bot {
                    self.bots
                        .iter_mut()
                        .find(|seat| seat.player() == *id)
                        .and_then(|seat| seat.finish(Some(result)))
                        .map(|finish| finish.choices)
                        .unwrap_or_default()
                } else {
                    Vec::new()
                };
                let _ = self.reports.send(RoomReport::Result {
                    admission,
                    match_id: self.id,
                    profile: result.profile.clone(),
                    bot: *bot,
                    bot_spec,
                    bot_choices,
                    fence,
                    result: result.clone(),
                    roster: Roster {
                        humans: self.fences.len() as u32,
                        bots: self.bots.len() as u32,
                    },
                });
            }
        }
    }

    pub(super) fn close(&mut self) -> bool {
        self.finish(false)
    }

    /// Closes the room. `interrupted` (the hosting layer's abort): events not yet reported are discarded, so no result is
    /// invented for a match that did not finish; seats and leases are freed exactly as for a normal close.
    pub(super) fn finish(&mut self, interrupted: bool) -> bool {
        if self.closed {
            return false;
        }
        self.closed = true;
        // Results emitted between steps (the last human's MIA leave) would otherwise die with the room.
        let pending = self.sim.take_events();
        if !interrupted {
            self.report_results(&pending);
        }
        if self.had_human {
            eprintln!(
                "{}",
                crate::lifecycle_log::match_end(
                    &crate::control::room_id_hex(self.id),
                    Instant::now().saturating_duration_since(self.opened_at),
                    self.humans.len()
                )
            );
        }
        let why = if interrupted {
            GoneReason::Aborted
        } else {
            GoneReason::MatchOver
        };
        if let Some(metrics) = self.alpha_metrics.take() {
            metrics.finish(why.as_str());
        }
        for h in std::mem::take(&mut self.humans) {
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Gone(why),
            });
            if let Some(o) = &h.outbox {
                o.close(RejectCode::Kicked);
                self.retire_outbox(o);
            }
            // A result this seat earned was reported above or earlier; the director keeps the profile until its write
            // lands (the write releases the lease), so `result_pending` stays about results still to come.
            let _ = self.reports.send(RoomReport::Left {
                admission: h.admission,
                match_id: self.id,
                profile: h.profile,
                fence: h.fence,
                result_pending: false,
            });
        }
        let replay = self.recorder.take().map(|r| Box::new(r.replay));
        let _ = self.reports.send(RoomReport::Closed {
            match_id: self.id,
            stats: self.stats(),
            final_hash: self.sim.state_hash(),
            replay,
        });
        false
    }

    /// The room reported `Closed`; its task must end.
    pub fn is_closed(&self) -> bool {
        self.closed
    }

    pub fn stats(&self) -> RoomStats {
        // Connections the room still holds count live; the ones it let go were folded in when it did.
        let mut outboxes = self.retired_outboxes;
        for o in self.humans.iter().filter_map(|h| h.outbox.as_deref()) {
            outboxes.add(o.counters());
        }
        RoomStats {
            ticks: self.step_us.count(),
            step_us_p50: self.step_us.quantile(0.5),
            step_us_p99: self.step_us.quantile(0.99),
            step_us_max: self.step_us.max(),
            late_wakes: self.pacer.late_wakes,
            dropped_steps: self.pacer.dropped,
            bytes_out: self.bytes_out,
            bytes_enqueued_total: outboxes.bytes_enqueued_total,
            fx_dropped: outboxes.fx_dropped,
            snapshots_skipped: outboxes.snapshots_skipped,
            snapshots_full: outboxes.snapshots_full,
            snapshots_delta: outboxes.snapshots_delta,
            snapshot_bytes_sent: outboxes.snapshot_bytes_sent,
            snapshot_refusals: self.snapshot_refusals,
            snapshot_refusals_logged: self.snapshot_refusals_logged,
            frame_refusals: self.frame_refusals,
            frame_refusals_logged: self.frame_refusals_logged,
            crashed: false,
            rtt_samples: self.rtt_samples,
            rtt_last_ms: self.rtt_last_ms,
            rtt_max_ms: self.rtt_max_ms,
            rewind_clamps: self.rewind_clamps,
        }
    }

    /// A call into the room panicked (see `guarded`): its connections are closed and `Closed` is reported (marked `crashed`),
    /// so the director frees every seat and gives the leases back instead of holding them until the process restarts.
    /// The simulation is not touched (its state is suspect: no results, no final hash). Never panics itself.
    pub(super) fn crashed(&mut self) {
        eprintln!(
            "[server] ERROR event=room_task_panicked room={} ticks={} humans={} action=reporting_closed",
            crate::control::room_id_hex(self.id),
            self.stats().ticks,
            self.humans.len()
        );
        for h in &self.humans {
            if let Some(o) = &h.outbox {
                o.close(RejectCode::Kicked);
            }
        }
        if let Some(metrics) = self.alpha_metrics.take() {
            metrics.finish("room_crashed");
        }
        let stats = RoomStats {
            crashed: true,
            ..self.stats()
        };
        let _ = self.reports.send(RoomReport::Closed {
            match_id: self.id,
            stats,
            final_hash: 0,
            replay: None,
        });
    }
}
