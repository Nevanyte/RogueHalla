//! Admission: protocol, content and credential checks, profile ownership, room choice, and one attempt's lease and seat bookkeeping.

use super::*;

impl Director {
    /// Admits a connection. Every refusal is decided before a seat is taken.
    pub async fn admit(
        &self,
        h: Hello,
        outbox: Arc<Outbox>,
        conn: u64,
    ) -> Result<(mpsc::Sender<RoomCmd>, JoinOk), (RejectCode, String)> {
        // A lobby can close into its run, or its last player can leave and the room close, between the pick and the join
        // (the room then refuses MatchFull, or its inbox is gone): that room is marked unjoinable, try another. Every
        // attempt shares one deadline for waiting on a profile mid-release, so the documented bound is the real one.
        let deadline = tokio::time::Instant::now() + LEAVING_WAIT;
        let mut tries = 0;
        loop {
            match self.admit_once(&h, outbox.clone(), conn, deadline).await {
                Err((RejectCode::MatchFull, reason))
                    if (reason == "lobby closed" || reason == "match closed") && tries < 3 =>
                {
                    tries += 1
                }
                other => return other,
            }
        }
    }

    pub(super) async fn admit_once(
        &self,
        h: &Hello,
        outbox: Arc<Outbox>,
        conn: u64,
        deadline: tokio::time::Instant,
    ) -> Result<(mpsc::Sender<RoomCmd>, JoinOk), (RejectCode, String)> {
        if h.protocol != PROTOCOL_VERSION {
            return Err((
                RejectCode::ProtocolMismatch,
                format!(
                    "server speaks protocol {PROTOCOL_VERSION}, client {}",
                    h.protocol
                ),
            ));
        }
        let content = rh_sim::content::content_hash();
        if h.content_hash != content {
            return Err((
                RejectCode::ContentMismatch,
                format!("server content {content:016x}"),
            ));
        }
        if let Some(session) = h.resume {
            let tx = {
                let inner = self.inner.lock_recover();
                inner
                    .sessions
                    .get(&session)
                    .and_then(|m| inner.rooms.get(m))
                    .map(|r| r.tx.clone())
            };
            let Some(tx) = tx else {
                return Err((RejectCode::NoSuchSession, "session expired".into()));
            };
            let (reply, rx) = oneshot::channel();
            tx.send(RoomCmd::Resume {
                session,
                outbox,
                conn,
                reply,
            })
            .await
            .map_err(|_| (RejectCode::NoSuchSession, "match over".into()))?;
            let ok = rx
                .await
                .map_err(|_| (RejectCode::NoSuchSession, "match over".into()))?
                .map_err(|c| (c, "resume refused".into()))?;
            return Ok((tx, ok));
        }
        if !profile_ok(&h.profile) {
            return Err((
                RejectCode::BadCredentials,
                "callsign must be 2-16 of A-Z a-z 0-9 _ -".into(),
            ));
        }
        if self.lifecycle.draining() {
            return Err((RejectCode::ServerDraining, "server draining".into()));
        }
        // Bounded like the lease acquisition below: a saturated pool would otherwise hold this Hello (and its socket) for the
        // pool's whole acquire timeout plus the query. The check only reads, so abandoning it at the bound is safe.
        let checked = tokio::time::timeout(
            self.cfg.leases.db_timeout,
            self.persistence
                .authenticate(&self.auth, &h.profile, &h.ticket),
        )
        .await;
        // The credential store could not answer, or not within the bound: that says nothing about the ticket, so it is
        // neither reported nor counted (the edge throttle counts only `BadCredentials`) as a bad credential. The wire has no
        // "unavailable" code; "try again shortly" is the honest existing one.
        let unavailable = || {
            (
                RejectCode::RateLimited,
                "server busy, storage unavailable; try again shortly".to_string(),
            )
        };
        let key = match checked {
            Ok(Ok(Some(key))) => key,
            Ok(Ok(None)) => return Err((RejectCode::BadCredentials, "bad credentials".into())),
            // The store's error stays in the (rate-limited) log.
            Ok(Err(e)) => {
                crate::api::log_internal("hello credential check", &e);
                return Err(unavailable());
            }
            // The log line carries the bound alone: no profile, no ticket.
            Err(_elapsed) => {
                eprintln!(
                    "[server] WARN event=credential_check_timed_out budget_ms={}",
                    self.cfg.leases.db_timeout.as_millis()
                );
                return Err(unavailable());
            }
        };
        // Hosted mode: an additional precondition after the credential check and before profile ownership and the fenced
        // lease (CONTROL_API.md section 4). It never replaces authentication and takes no lease. The admit is consumed
        // below, atomically with the seat reservation; this early look only refuses before any wait.
        if self.cfg.require_admit
            && !self
                .inner
                .lock_recover()
                .control
                .has_admit(&h.profile, tokio::time::Instant::now())
        {
            return Err((RejectCode::BadCredentials, "not admitted".into()));
        }
        // A player who just left (or whose match just closed, or whose previous attempt here just failed) has its lease
        // give-back in flight: wait for it instead of refusing, so a quick rejoin is deterministic. A profile with a live
        // seat, or with another admission in flight, is refused below as before.
        self.wait_while_leaving(&key, deadline).await;
        // Pick (or open) a lobby, reserving the profile in the same critical section so two connections cannot both pass.
        let mut prepared = None;
        let (match_id, tx, gen) = loop {
            let reserved = 'reserve: {
                let mut inner = self.inner.lock_recover();
                if self.lifecycle.draining() {
                    return Err((RejectCode::ServerDraining, "server draining".into()));
                }
                if inner.profiles.contains_key(&key) {
                    return Err((
                        RejectCode::ProfileInUse,
                        "callsign already in a match".into(),
                    ));
                }
                let next_gen = inner.gens + 1;
                let (match_id, tx) = if self.cfg.require_admit {
                    // Hosted mode: the room is the one the admit names (no automatic lobby placement, no room opened here).
                    // The admit is consumed here, in the critical section that reserves the seat; any later failure of this
                    // admission restores it (`unwind`).
                    let Some(taken) =
                        inner
                            .control
                            .take_admit(&h.profile, tokio::time::Instant::now(), next_gen)
                    else {
                        return Err((RejectCode::BadCredentials, "not admitted".into()));
                    };
                    let target = inner
                        .rooms
                        .get(&taken.room)
                        .map(|r| (r.phase == Phase::Lobby, r.tx.clone()));
                    match target {
                        Some((true, tx)) => (taken.room, tx),
                        other => {
                            inner.control.restore_gen(next_gen);
                            let why = if other.is_none() {
                                "match closed"
                            } else {
                                "match in progress"
                            };
                            return Err((RejectCode::MatchFull, why.into()));
                        }
                    }
                } else {
                    let open = open_lobby(
                        &inner.rooms,
                        if self.cfg.sim.training {
                            1
                        } else {
                            rh_sim::constants::MAX_PLAYERS
                        },
                    )
                    .and_then(|id| inner.rooms.get(&id).map(|r| (id, r.tx.clone())));
                    match open {
                        Some(x) => x,
                        None => {
                            if inner.rooms.len() >= self.cfg.max_rooms {
                                return Err((RejectCode::MatchFull, "no capacity".into()));
                            }
                            if let Some(room) = prepared.take() {
                                self.activate_room(&mut inner, room)
                            } else if self.needs_board_room() {
                                break 'reserve None;
                            } else {
                                self.spawn_room(&mut inner, self.cfg.fill_bots_to)
                            }
                        }
                    }
                };
                inner.gens += 1;
                let gen = inner.gens;
                inner.profiles.insert(
                    key.clone(),
                    ProfileSeat {
                        renewal: None,
                        gen,
                        match_id,
                        fence: 0,
                        state: SeatState::Reserved,
                        result_due: false,
                        writes: 0,
                        lease: LeaseState::Held,
                    },
                );
                if let Some(r) = inner.rooms.get_mut(&match_id) {
                    r.reserved += 1;
                }
                Some((match_id, tx, gen))
            };
            if let Some(reserved) = reserved {
                break reserved;
            }
            prepared = Some(
                self.prepare_board_room_async(self.cfg.fill_bots_to)
                    .await
                    .map_err(|error| {
                        crate::api::log_internal("quest map preparation", &error);
                        (
                            RejectCode::RateLimited,
                            "quest map unavailable; try again shortly".into(),
                        )
                    })?,
            );
        };
        // Cross-host session ownership: a fenced lease in durable storage (off the tick; before the seat is taken).
        let mut pending = PendingAdmission {
            director: self.me.clone(),
            runtime: tokio::runtime::Handle::current(),
            key: key.clone(),
            gen,
            match_id,
            fence: None,
            reply: None,
            clock: None,
            armed: true,
        };
        let policy = self.cfg.leases;
        let started = tokio::time::Instant::now();
        let acquisition = self
            .persistence
            .acquire_live(&key, match_id, &h.profile, policy);
        let acquisition = tokio::time::timeout(policy.db_timeout, acquisition)
            .await
            .unwrap_or_else(|_| Err(LeaseError::timed_out()));
        let lease = match acquisition {
            Ok(lease) => lease,
            Err(e) => {
                pending.armed = false;
                self.unwind(&key, gen, match_id, None);
                return Err(match e {
                    LeaseError::Held(e) => (RejectCode::ProfileInUse, e),
                    // Storage trouble says nothing about the callsign; the wire has no "unavailable" code (appending one
                    // is a protocol change), so the closest honest one is "try again shortly" with an accurate reason.
                    LeaseError::Unavailable(e) => {
                        // The detail (sqlx/Postgres text) stays in the server log; the client gets a fixed string.
                        eprintln!("[server] WARN event=lease_unavailable detail={e:?}");
                        (
                            RejectCode::RateLimited,
                            "server busy, storage unavailable; try again shortly".to_string(),
                        )
                    }
                });
            }
        };
        let fence = lease.fence;
        pending.fence = Some(fence);
        if let Some(s) = self.inner.lock_recover().profiles.get_mut(&key) {
            if s.gen == gen {
                s.fence = fence;
            }
        }
        let (reply, rx) = oneshot::channel();
        let faction = h.faction.and_then(FactionId::from_code);
        let deadline = started + policy.ttl;
        let (validity, clock) = tokio::sync::watch::channel(deadline);
        let request = RoomCmd::JoinLeased {
            admission: Admission {
                gen,
                profile_id: key.id(),
            },
            profile: h.profile.clone(),
            faction,
            outbox,
            conn,
            fence,
            deadline: clock,
            reply,
        };
        match tokio::time::timeout_at(deadline, tx.send(request)).await {
            Ok(Ok(())) => {}
            failed => {
                pending.armed = false;
                self.unwind(&key, gen, match_id, Some(fence));
                if failed.is_ok() {
                    self.mark_unjoinable(match_id);
                    return Err((RejectCode::MatchFull, "match closed".into()));
                }
                return Err((
                    RejectCode::ProfileInUse,
                    "lease expired before seating".into(),
                ));
            }
        }
        pending.clock = Some(validity.clone());
        pending.reply = Some(rx);
        self.start_renewals(key.clone(), gen, match_id, lease, started, validity);
        let outcome = pending.reply.as_mut().expect("join sent").await;
        pending.armed = false;
        match outcome {
            Ok(Ok(ok)) => {
                let mut inner = self.inner.lock_recover();
                let room_gone = !inner.rooms.contains_key(&match_id);
                let mut orphaned = false;
                if let Some(s) = inner.profiles.get_mut(&key).filter(|s| s.gen == gen) {
                    if s.state == SeatState::Reserved {
                        // The room crashed after seating us and its `Closed` (which skips reservations) is already
                        // handled: nothing else would free this seat.
                        orphaned = room_gone;
                        s.state = if room_gone {
                            SeatState::Leaving
                        } else {
                            SeatState::Seated
                        };
                    }
                    // Otherwise the seat's `Left` was handled before this reply: the entry is `Leaving` and its own
                    // give-back frees it.
                }
                if orphaned {
                    self.advance(&mut inner, &key);
                }
                // A room that already closed has had its sessions swept: nothing would ever remove this one.
                if !room_gone {
                    inner.sessions.insert(ok.session, match_id);
                }
                Ok((tx, ok))
            }
            Ok(Err(code)) => {
                self.unwind(&key, gen, match_id, Some(fence));
                if code == RejectCode::MatchFull {
                    // The lobby closed into its run: never pick it again; admit() tries another room.
                    self.mark_unjoinable(match_id);
                    return Err((code, "lobby closed".into()));
                }
                Err((code, "join refused".into()))
            }
            Err(_) => {
                self.unwind(&key, gen, match_id, Some(fence));
                self.mark_unjoinable(match_id);
                Err((RejectCode::MatchFull, "match closed".into()))
            }
        }
    }
}
