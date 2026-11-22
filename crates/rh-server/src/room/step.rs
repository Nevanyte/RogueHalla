//! One wake: the grid slot's step, the events it produced, and their publication to every seat's outbox.

use super::*;

impl Room {
    pub fn next_deadline(&self) -> Instant {
        self.pacer.next_deadline()
    }

    /// Runs the steps the grid owes and publishes once. Returns false when the room should close.
    pub fn wake(&mut self, now: Instant) -> bool {
        self.expire_leases(now);
        // Reconnect grace: a seat whose client did not come back is a leave (MIA in a run).
        let expired: Vec<PlayerId> = self
            .humans
            .iter()
            .filter(|h| {
                h.dropped_at
                    .is_some_and(|t| now.duration_since(t) >= self.reconnect_grace)
            })
            .map(|h| h.player)
            .collect();
        for p in expired {
            if let Some(h) = self.humans.iter().find(|h| h.player == p) {
                eprintln!(
                    "{}",
                    crate::lifecycle_log::grace_expired(
                        &crate::control::room_id_hex(self.id),
                        &h.profile
                    )
                );
            }
            self.leave(p, GoneReason::GraceExpired);
        }
        // A training room whose seat stopped sending accepted inputs closes: there is no run to settle and nothing to bank.
        if self.training {
            let since = self.last_input_at.unwrap_or(self.opened_at);
            if now.duration_since(since) >= self.training_idle {
                return self.close();
            }
        }
        if self.humans.is_empty()
            && self.sim.phase() != Phase::Ended
            && (self.had_human
                || self.draining
                || now.duration_since(self.opened_at) >= UNCLAIMED_TIMEOUT)
        {
            // Nobody left to play for: a lobby closes; a run's remaining players are all bots or gone. A room nobody has
            // joined yet waits for its first player (its admission may be acquiring a durable lease off the tick), unless
            // the server is draining: no admission can seat after that, so an empty room has nothing left to wait for.
            return self.close();
        }
        let slot = self.pacer.next_deadline();
        let dropped_before = self.pacer.dropped;
        let steps = self.pacer.due(now);
        if steps == 0 {
            return true;
        }
        #[cfg(test)]
        if !self.stall.is_zero() {
            std::thread::sleep(self.stall);
        }
        // One add per shared counter for this wake (the histograms add one bucket per value).
        self.metrics.wakes.fetch_add(1, Ordering::Relaxed);
        self.metrics
            .steps
            .fetch_add(u64::from(steps), Ordering::Relaxed);
        let dropped = self.pacer.dropped - dropped_before;
        if dropped > 0 {
            self.metrics
                .dropped_steps
                .fetch_add(dropped, Ordering::Relaxed);
        }
        self.metrics.note_wake(
            now.saturating_duration_since(slot),
            steps,
            self.pacer.period(),
        );
        self.wake_events.clear();
        for _ in 0..steps {
            self.fill_step();
            let t = std::time::Instant::now();
            // Input age: every Input message the room took since the last step is applied by this one.
            for h in &mut self.humans {
                if let Some(at) = h.input_received.take() {
                    self.metrics.input_age_us.record(
                        t.saturating_duration_since(at)
                            .as_micros()
                            .min(u128::from(u32::MAX)) as u32,
                    );
                }
            }
            self.last_step_start = Some(t);
            let mut proposed: Vec<(PlayerId, rh_sim::PublicCommand)> = Vec::new();
            drive_seats_queued(
                &mut self.sim,
                &mut self.bots,
                &self.last_events,
                &mut self.bot_sight,
                |player, cmd| proposed.push((player, cmd)),
            );
            self.enqueue_bot_commands(proposed);
            self.deliver_bot_commands();
            self.expire_leases(Instant::now().max(now));
            self.step_events.clear();
            self.sim.step_into(&mut self.step_events);
            let us = t.elapsed().as_micros().min(u32::MAX as u128) as u32;
            if let Some(r) = self.recorder.as_mut() {
                r.stepped(&self.sim);
            }
            self.step_us.record(us);
            self.metrics.step_us.record(us);
            rh_policy::feed_seats(&self.sim, &mut self.bots);
            self.log_brain_fallbacks();
            for h in &mut self.humans {
                if h.outbox.is_none() {
                    h.fx.rebind(&self.sim, h.player);
                    h.carry.clear();
                    continue;
                }
                h.fx.set_eye(self.sim.spectate_eye(h.player, h.items.spectate()));
                match h.fx.feed(self.sim.fx_report()).and_then(|status| {
                    if status != rh_sim::FeedStatus::Duplicate {
                        h.fx.append_into(&self.sim, h.player, &mut h.carry)?;
                    }
                    Ok(status)
                }) {
                    Ok(rh_sim::FeedStatus::Duplicate) => {}
                    Ok(_) => {}
                    Err(e) => {
                        h.carry.clear();
                        self.frame_refusals += 1;
                        if self.frame_refusals_logged == 0 {
                            self.frame_refusals_logged += 1;
                            eprintln!("[room] temporal projection refused: {e:?}");
                        }
                    }
                }
            }
            self.wake_events.extend_from_slice(&self.step_events);
            std::mem::swap(&mut self.last_events, &mut self.step_events);
        }
        self.clock
            .store(self.sim.server_time().to_bits(), Ordering::Relaxed);
        let t = std::time::Instant::now();
        // The buffer leaves the room for the call (publish borrows the room mutably) and comes back.
        let all_events = std::mem::take(&mut self.wake_events);
        // A training room never resolves a run: a between-tick down event is not a result let alone banked, and no result
        // frame is ever sent to its seat.
        let all_events = if self.training {
            let mut kept = all_events;
            kept.retain(|e| !matches!(e, Event::Resolved { .. }));
            kept
        } else {
            all_events
        };
        self.keep_results(&all_events);
        self.publish(&all_events);
        self.metrics
            .publish_us
            .record(t.elapsed().as_micros().min(u128::from(u32::MAX)) as u32);
        self.report_results(&all_events);
        self.wake_events = all_events;
        if self.sim.phase() != self.reported_phase {
            // Every phase change reaches the director (a lobby that closed into a run must stop receiving joins).
            self.reported_phase = self.sim.phase();
            let _ = self.reports.send(RoomReport::Phase {
                match_id: self.id,
                phase: self.sim.phase(),
            });
        }
        if self.sim.phase() == Phase::Ended && self.ended_at.is_none() {
            self.ended_at = Some(now);
        }
        if let Some(t) = self.ended_at {
            if now.duration_since(t) >= self.linger {
                return self.close();
            }
        }
        true
    }

    /// One tick's frames for every connected human. `events`: every event of the wake; the observation's own events are
    /// those of the last step (`last_events`) plus what each human's `carry` gathered from the earlier steps.
    pub(super) fn publish(&mut self, events: &[Event]) {
        let (mut bytes_total, mut pushes) = (0u64, 0u64);
        for h in &mut self.humans {
            let Some(out) = &h.outbox else {
                h.carry.clear();
                continue;
            };
            // A forced replacement renews all capability namespaces off to the side. Failed
            // encoding or enqueue preserves the live epoch; successful delivery installs both.
            let resync_requested = out.state_resync_requested();
            let mut renewal = h
                .collections
                .resync_due(self.sim.tick(), resync_requested)
                .then(|| h.items.renewed());
            let observer = match renewal.as_mut() {
                Some(observer) => observer,
                None => &mut h.items,
            };
            let (frames, offer) = match self.publisher.frames_prepared(
                observer,
                h.spare.take(),
                &self.sim,
                h.ack_input,
                &[],
                &mut h.carry,
                events,
            ) {
                Ok(prepared) => prepared,
                Err(error) => {
                    if self.frame_refusals == 0 {
                        self.frame_refusals_logged += 1;
                        eprintln!("[room] unsupported observation: {error:?}");
                    }
                    self.frame_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            if let Some(error) = frames.refused {
                if self.frame_refusals == 0 {
                    self.frame_refusals_logged += 1;
                    eprintln!("[room] frame not sent: {error}");
                }
                self.frame_refusals += 1;
                // A refused part of this tick cannot publish a new capability epoch or baseline.
                out.close(RejectCode::Malformed);
                continue;
            }
            let snap = match frames.snapshot {
                Ok(snap) => snap,
                Err(error) => {
                    if self.snapshot_refusals == 0 {
                        self.snapshot_refusals_logged += 1;
                        eprintln!("[room] snapshot not sent: {error}");
                    }
                    self.snapshot_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            let mut state = match h.collections.prepare(
                self.publisher.observation(),
                offer.props_changed(),
                offer.rewards_changed(),
                resync_requested,
            ) {
                Ok(pending) => pending,
                Err(error) => {
                    if self.frame_refusals == 0 {
                        self.frame_refusals_logged += 1;
                        eprintln!("[room] reliable state not sent: {error}");
                    }
                    self.frame_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            let bytes = [&state.frame, &frames.important, &frames.fx]
                .into_iter()
                .flatten()
                .map(Vec::len)
                .sum::<usize>()
                + snap.min_len();
            let (spare, accepted) = out.push_tick_stamped(
                self.last_step_start,
                state.frame.take(),
                frames.important,
                frames.fx,
                Some(snap),
            );
            h.spare = spare;
            if accepted {
                let forced = state.commit(&mut h.collections);
                offer.commit();
                if let Some(observer) = renewal {
                    h.items = observer;
                }
                if forced {
                    out.state_resync_answered();
                }
                self.bytes_out += bytes as u64;
                bytes_total += bytes as u64;
                pushes += 1;
            }
            // Failed reliable enqueue drops both prepared guards: no handles or baseline advance.
        }
        if pushes > 0 {
            self.metrics
                .bytes_out
                .fetch_add(bytes_total, Ordering::Relaxed);
            self.metrics
                .outbox_pushes
                .fetch_add(pushes, Ordering::Relaxed);
        }
    }
}
