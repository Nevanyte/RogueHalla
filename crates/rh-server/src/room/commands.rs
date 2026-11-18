//! The room's command intake: every [`RoomCmd`] a connection or the director sends, applied between steps.

use super::*;

impl Room {
    pub fn handle(&mut self, cmd: RoomCmd, now: Instant) {
        let now = now.max(Instant::now());
        self.expire_leases(now);
        match cmd {
            RoomCmd::Join {
                profile,
                faction,
                outbox,
                conn,
                fence,
                reply,
            } => {
                let r = self.join(profile, faction, outbox, conn, fence, None);
                let _ = reply.send(r);
            }
            RoomCmd::JoinLeased {
                admission,
                profile,
                faction,
                outbox,
                conn,
                fence,
                deadline,
                reply,
            } => {
                let result = if *deadline.borrow() <= now {
                    Err(RejectCode::ProfileInUse)
                } else {
                    self.join(
                        profile,
                        faction,
                        outbox,
                        conn,
                        fence,
                        Some((admission, deadline)),
                    )
                };
                let _ = reply.send(result);
            }
            RoomCmd::Resume {
                session,
                outbox,
                conn,
                reply,
            } => {
                let r = match self.humans.iter_mut().find(|h| h.session == session) {
                    None => Err(RejectCode::NoSuchSession),
                    Some(h) => {
                        let old = h.outbox.replace(outbox.clone());
                        // From now on only the new connection speaks for this seat.
                        h.conn = conn;
                        h.dropped_at = None;
                        // A new connection is a new path: its first Pong measures it, the old estimate is not inherited.
                        h.rtt = RttEstimator::new();
                        h.fx.rebind(&self.sim, h.player);
                        h.carry.clear();
                        h.items.reconnect();
                        h.collections.reconnect();
                        let (player, session) = (h.player, h.session);
                        let seat_profile = h.profile.clone();
                        let resolved = h.result.clone();
                        if let Some(old) = old {
                            old.close(RejectCode::Kicked);
                            self.retire_outbox(&old);
                        }
                        self.record(ReplayInput::Dropped {
                            player,
                            dropped: false,
                        });
                        self.sim.mark_dropped(player, false);
                        let _ = self.reports.send(RoomReport::Seat {
                            match_id: self.id,
                            profile: seat_profile,
                            event: SeatEvent::Back,
                        });
                        // The run this player already resolved: the one reliable event that is not state the client can read
                        // back, so it is queued behind the Welcome (which the connection writes first).
                        if let Some(r) = resolved {
                            let frame = ServerMsg::Events {
                                tick: self.sim.tick(),
                                events: vec![WireEvent::Result(r)],
                            }
                            .try_encode();
                            if let Ok(frame) = frame {
                                outbox.important(frame);
                            }
                        }
                        // A fresh Welcome is the resync: full map and the current tick; the next snapshot follows.
                        Ok(JoinOk {
                            player,
                            session,
                            welcome: self.welcome(player, session),
                            clock: self.clock.clone(),
                        })
                    }
                };
                let _ = reply.send(r);
            }
            RoomCmd::Input {
                player,
                conn,
                seq,
                commands,
                received,
            } => {
                if let Some(index) = self
                    .humans
                    .iter()
                    .position(|h| h.player == player && h.conn == conn)
                {
                    self.humans[index].ack_input = seq;
                    self.humans[index].input_received.get_or_insert(received);
                    // The clamp is written for the sim time this batch will be applied at: the room applies commands
                    // between steps and the next step adds one tick first, so `note_lag` in the sim will see
                    // `now_apply` as its `now`. P-NET-1 Phase 3: bound an attack render time by the RTT the server
                    // measured for this connection (never by the client's claim), before `Sim::submit`. Cast render-time
                    // metadata receives the same bound, but Wish effects are not rewound by it.
                    let now_apply = self.sim.server_time() + TICK_MS;
                    let mut accepted = 0u32;
                    let mut clamps = RewindClampStats::default();
                    for c in commands {
                        let (proposal, clamped) = clamp_render_time_noted(
                            wire::command(c),
                            now_apply,
                            &self.humans[index].rtt,
                            &mut clamps,
                        );
                        if clamped {
                            self.rewind_clamps += 1;
                        }
                        // Resolve only within this seat's delivered offer, then use the shared
                        // validator/budget/submit path. Target is an observer-issued reference,
                        // never a private-ID fallback. Misses are recorded ordinary commands.
                        let resolved = self.humans[index].items.resolve(&self.sim, proposal);
                        if let Ok(cmd) = resolved {
                            self.record(ReplayInput::Command { player, cmd });
                            if self.sim.submit(player, cmd).is_ok() {
                                accepted += 1;
                            }
                        }
                    }
                    self.metrics.note_rewind_clamps(&clamps);
                    // The training idle clock runs on ACCEPTED inputs (a rejected/no-op command does not keep the room up).
                    if accepted > 0 {
                        self.last_input_at = Some(now);
                    }
                }
            }
            RoomCmd::RttSample {
                player,
                conn,
                rtt_ms,
            } => {
                // Only the connection that owns the seat may move its window; a replaced connection's late Pongs are
                // ignored, so a resume starts measuring the new path from scratch.
                if let Some(h) = self
                    .humans
                    .iter_mut()
                    .find(|h| h.player == player && h.conn == conn)
                {
                    h.rtt.sample(rtt_ms);
                    let estimate = h.rtt.estimate_ms();
                    rtt::note_estimate(self.id, player, estimate);
                    self.rtt_samples += 1;
                    self.rtt_last_ms = estimate;
                    self.rtt_max_ms = self.rtt_max_ms.max(estimate);
                }
            }
            RoomCmd::Disconnected { player, conn } => {
                // A replaced connection's late disconnect must not detach the seat its resume took over.
                if let Some(h) = self
                    .humans
                    .iter_mut()
                    .find(|h| h.player == player && h.conn == conn)
                {
                    let old = h.outbox.take();
                    h.dropped_at = Some(now);
                    self.record(ReplayInput::Dropped {
                        player,
                        dropped: true,
                    });
                    self.sim.mark_dropped(player, true);
                    if let Some(old) = old {
                        self.retire_outbox(&old);
                    }
                    if let Some(h) = self.humans.iter().find(|h| h.player == player) {
                        let _ = self.reports.send(RoomReport::Seat {
                            match_id: self.id,
                            profile: h.profile.clone(),
                            event: SeatEvent::Away { at: now },
                        });
                    }
                }
            }
            RoomCmd::Leave { player, conn } => {
                if self
                    .humans
                    .iter()
                    .any(|h| h.player == player && h.conn == conn)
                {
                    self.leave(player, GoneReason::Left);
                }
            }
            RoomCmd::Kick { profile } => {
                if let Some(player) = self
                    .humans
                    .iter()
                    .find(|h| h.profile.eq_ignore_ascii_case(&profile))
                    .map(|h| h.player)
                {
                    self.leave(player, GoneReason::Kicked);
                }
            }
            RoomCmd::Abort => {
                self.finish(true);
            }
            RoomCmd::Drain => self.draining = true,
            RoomCmd::DrainEnd => {
                self.draining = true;
                let seated: Vec<PlayerId> = self.humans.iter().map(|h| h.player).collect();
                for player in seated {
                    self.leave(player, GoneReason::Aborted);
                }
                self.finish(false);
            }
            #[cfg(test)]
            RoomCmd::Crash => panic!("test: the room task panics"),
        }
    }
}
