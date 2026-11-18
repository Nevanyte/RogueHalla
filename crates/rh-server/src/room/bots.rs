//! Bot fill: seating fill bots, their delayed command delivery, the fill step and the Brain fallback log.

use super::task::fault_kind;
use super::*;

impl Room {
    /// Scripted bots on the same seat contract as future policies (rh-policy). Bots never close a lobby: only humans count.
    pub(super) fn fill_bots(&mut self) {
        while self.humans_present() > 0 && self.sim.players().len() < self.fill_to {
            self.bot_seq += 1;
            let name = format!("bot{}", self.bot_seq);
            let Some(id) = self.sim.add_player(&name, None, true) else {
                break;
            };
            self.report_bot(&name, true);
            let seed = match self.fixed_bot_seed {
                Some(s) => s.wrapping_add(self.bot_seq),
                None => u64::from(random_u32()) << 32 | u64::from(random_u32()),
            };
            let interval = 2 + (self.bot_seq % 3) as u32;
            self.record(ReplayInput::Join {
                profile: name,
                faction: None,
                bot: true,
                policy: Some(BotPolicy {
                    name: "brawler".into(),
                    interval,
                    seed,
                }),
            });
            self.bots.push(Seat::new(
                &MatchStart::of(&self.sim, id, seed),
                Box::new(scripted::Brawler::new(interval)),
                Box::new(scripted::Idle),
            ));
            self.bots
                .last_mut()
                .expect("new bot")
                .bind_fx(&self.sim)
                .expect("new bot is unbound");
        }
    }

    /// Opt into the legacy manager's lobby fill (BotManager.ts): bots arrive on a seeded schedule through the countdown,
    /// the closing step seats whoever has not arrived, and a joining human takes the newest bot's seat. Without it the room
    /// keeps its immediate placeholder fill above (the default for tests, benches and rooms that do not configure bots).
    ///
    /// The policy's seed comes from the room's fixed bot seed when set (tests, replay determinism), else OS entropy: the
    /// fill seed is not a match secret (docs/CONTRACTS.md C-RNG does not cover bot identity).
    pub fn with_bot_fill(mut self, cfg: BotFillConfig) -> Self {
        // Source BOTS_SEED seeds every manager stream; the rooms' fixed bot seed (tests, replays) wins over it.
        let seed = self
            .fixed_bot_seed
            .or_else(|| cfg.seed.map(u64::from))
            .unwrap_or_else(|| (u64::from(random_u32()) << 32) | u64::from(random_u32()));
        self.line_rng = DelayLine::<rh_sim::PublicCommand>::rng(seed as u32);
        self.fill = Some(BotFillState::new(cfg, seed));
        self
    }

    pub fn with_learning(mut self, sink: Arc<dyn crate::bot_learning::LearningSink>) -> Self {
        if let Some(fill) = &mut self.fill {
            fill.set_learning_sink(sink);
        }
        self
    }

    /// The manager's read-only profile-name seam (source isNameTaken): callsigns real profiles own, lower-cased. The set
    /// is a snapshot the director refreshes per lobby; bots never read or write a profile.
    pub fn with_bot_names(mut self, names: Arc<std::collections::HashSet<String>>) -> Self {
        self.taken_names = names;
        self
    }

    /// The delay line's send half (source BotManager.deliver): a fill bot's proposal gets its arrival from the manager's
    /// line stream; a seat with no fill spec (the immediate placeholder fill) submits now, as before.
    pub(super) fn enqueue_bot_commands(
        &mut self,
        proposed: Vec<(PlayerId, rh_sim::PublicCommand)>,
    ) {
        if proposed.is_empty() {
            return;
        }
        let (latency, jitter) = self.fill.as_ref().map_or((0., 0.), |fill| {
            (fill.config().latency_ms, fill.config().jitter_ms)
        });
        let at = self.sim.server_time();
        for (player, cmd) in proposed {
            if let Some(pos) = self.bot_lines.iter().position(|(p, _)| *p == player) {
                let accepted = self.bot_lines[pos]
                    .1
                    .enqueue(&mut self.line_rng, at, latency, jitter, cmd)
                    .is_some();
                if accepted {
                    self.bot_sent += 1;
                } else {
                    // The line is at its 256-command cap (source MAX_QUEUED): a delivery-stage rejection, not an intake
                    // refusal. Kept apart from bot_refused so the source's delivery-time accounting stays legible.
                    self.bot_line_dropped += 1;
                }
            } else {
                self.submit_bot_now(player, cmd);
            }
        }
    }

    /// The delay line's delivery half (source BotManager.beforeStep, called with the upcoming step's time): a command
    /// arrives, is resolved against the state at that step and goes through validate + submit, the intake a human's
    /// message takes.
    pub(super) fn deliver_bot_commands(&mut self) {
        if self.bot_lines.is_empty() {
            return;
        }
        let step_t = self.sim.server_time() + rh_sim::constants::TICK_MS;
        let mut due: Vec<(PlayerId, rh_sim::PublicCommand)> = Vec::new();
        for (player, line) in &mut self.bot_lines {
            let mut out = Vec::new();
            line.pop_due(step_t, &mut out);
            due.extend(out.into_iter().map(|cmd| (*player, cmd)));
        }
        for (player, cmd) in due {
            if self.sim.player(player).is_none() {
                self.bot_lost += 1;
                continue;
            }
            self.submit_bot_now(player, cmd);
        }
    }

    pub(super) fn submit_bot_now(&mut self, player: PlayerId, cmd: rh_sim::PublicCommand) {
        let Some(index) = self.bots.iter().position(|seat| seat.player() == player) else {
            self.bot_lost += 1;
            return;
        };
        let Some(command) = self.bots[index].resolve(&self.sim, cmd) else {
            self.bot_refused += 1;
            self.bots[index].note_delivery(false);
            return;
        };
        if let Some(r) = self.recorder.as_mut() {
            r.record(ReplayInput::Command {
                player,
                cmd: command,
            });
        }
        match self.sim.submit(player, command) {
            Ok(()) => {
                self.bot_applied += 1;
                self.bots[index].note_delivery(true);
            }
            Err(_) => {
                self.bot_refused += 1;
                self.bots[index].note_delivery(false);
            }
        }
    }

    /// The delay-line counters (source manager stats' delivery half: sent, applied, refused, lost), for telemetry.
    /// refused is delivery-time intake refusal only (see the field docs); line-cap drops are reported by
    /// bot_line_dropped and are not part of these four.
    pub fn bot_delivery(&self) -> (u64, u64, u64, u64) {
        (
            self.bot_sent,
            self.bot_applied,
            self.bot_refused,
            self.bot_lost,
        )
    }

    /// Commands the delay line dropped because it already held its 256-command cap (source MAX_QUEUED). The source reports
    /// no such counter at all; it is kept here so the delivery-time refused count stays the source's meaning.
    pub fn bot_line_dropped(&self) -> u64 {
        self.bot_line_dropped
    }

    /// The seated fill bots' telemetry tags (id, callsign, tier, persona, faction, seed). The lobby HUD's head count and
    /// its presentation stay with the client/coordinator; this is the native per-bot telemetry surface.
    /// The `[bots]` lines this room printed (tests only).
    #[cfg(test)]
    pub(super) fn log_lines(&self) -> &[String] {
        &self.log_sink
    }

    pub fn bot_specs(&self) -> &[(PlayerId, BotSeatSpec)] {
        &self.bot_specs
    }

    /// One lobby step of the fill policy, before the step it applies to (the manager's lobbyTick and, on the closing
    /// step, lobbyClosing): draws the arrivals that are due and seats them, then seats whoever is still missing when the
    /// lobby is about to close into the raid.
    pub(super) fn fill_step(&mut self) {
        if self.fill.is_none() || self.sim.phase() != Phase::Lobby {
            return;
        }
        let now = self.sim.server_time();
        let ends = self.sim.lobby_ends_at();
        let mut factions = [0u32; 5];
        let mut names_lower = Vec::with_capacity(self.sim.players().len());
        let mut humans = 0usize;
        for player in self.sim.players() {
            names_lower.push(player.seat.name.to_lowercase());
            if !player.seat.bot {
                humans += 1;
            }
            let faction = player.faction();
            if let Some(index) = FactionId::ALL.iter().position(|known| *known == faction) {
                factions[index] += 1;
            }
        }
        let players = self.sim.players().len();
        let taken_names = self.taken_names.clone();
        let taken = |name: &str| taken_names.contains(&name.to_ascii_lowercase());
        let view = FillView {
            players,
            humans,
            factions,
            names_lower,
            taken: &taken,
        };
        // The step closes the lobby when the countdown is out, when the lobby is full, or when enough humans are in and
        // the fill time is zero (the sim's updateLobby rule). The manager seats its remaining arrivals on that step.
        let closing = humans >= rh_sim::constants::MAX_PLAYERS
            || now >= ends
            || (humans >= self.min_players && self.fill_seconds <= 0.);
        let mut specs: Vec<BotSeatSpec> = self
            .fill
            .as_mut()
            .map(|fill| fill.lobby_tick(now, ends, &view))
            .unwrap_or_default();
        if closing {
            if let Some(fill) = self.fill.as_mut() {
                specs.extend(fill.lobby_closing(&view));
            }
        }
        for spec in specs {
            self.seat_fill_bot(spec);
        }
    }

    /// Seats one bot the fill policy drew: its callsign, faction, tier and persona go on the normal seat contract, so it
    /// perceives only through observations and acts only through validate + submit; it never touches a profile or quest.
    pub(super) fn seat_fill_bot(&mut self, spec: BotSeatSpec) {
        let name = spec.name.clone();
        let Some(id) = self.sim.add_player(&name, Some(spec.faction), true) else {
            return;
        };
        self.report_bot(&name, true);
        let brain = self
            .fill
            .as_ref()
            .filter(|fill| fill.config().brain == crate::bot_fill::BotBrain::Full)
            .map(|fill| fill.config().learned.clone());
        let budgeted = brain.is_some();
        let (policy, fallback, replay_name): (Box<dyn Policy>, Box<dyn Policy>, &str) = match brain
        {
            // P-BOT-BRAIN-LIVE-1: the Brain plays the seat; a faulted Brain hands it to HumanBot, not to Idle.
            Some(learned) => (
                Box::new(
                    BrainBot::new(
                        spec.tier,
                        spec.persona,
                        rh_policy::bots::brain_features(self.sim.config(), self.sim.shared_arena()),
                    )
                    .with_learned(learned),
                ),
                Box::new(HumanBot::new(spec.tier, spec.persona)),
                "brain",
            ),
            None => (
                Box::new(HumanBot::new(spec.tier, spec.persona)),
                Box::new(scripted::Idle),
                "human",
            ),
        };
        let interval = policy.spec().decision_interval_ticks;
        self.record(ReplayInput::Join {
            profile: name,
            faction: Some(spec.faction),
            bot: true,
            policy: Some(BotPolicy {
                name: replay_name.into(),
                interval,
                seed: u64::from(spec.seed),
            }),
        });
        let mut seat = Seat::new(
            &MatchStart::of(&self.sim, id, u64::from(spec.seed)),
            policy,
            fallback,
        );
        if budgeted {
            seat.budget = Some(crate::bot_fill::BRAIN_DECISION_BUDGET);
        }
        seat.bind_fx(&self.sim).expect("new bot is unbound");
        self.bots.push(seat);
        self.bot_specs.push((id, spec));
        self.bot_lines.push((id, DelayLine::new()));
    }

    /// Prints one `[bots]` line (and keeps it for tests).
    pub(super) fn bots_log(&mut self, line: String) {
        eprintln!("{line}");
        #[cfg(test)]
        self.log_sink.push(line);
    }

    /// P-BOT-BRAIN-FAULT-LOG-1: a Brain seat's first fault logs one line (and counts); the steady path scans a handful of
    /// seats and allocates nothing. Also the once-per-match line saying how many Brain seats the match started with.
    pub(super) fn log_brain_fallbacks(&mut self) {
        if !self.brain_start_logged && self.sim.phase() == Phase::Active {
            self.brain_start_logged = true;
            let k = self.bots.iter().filter(|s| s.budget.is_some()).count();
            if k > 0 {
                let line = format!(
                    "[bots] match={} fill bots: {k} brain",
                    crate::control::room_id_hex(self.id)
                );
                self.bots_log(line);
            }
        }
        for i in 0..self.bots.len() {
            let seat = &self.bots[i];
            let Some(fault) = seat.fault.as_ref().filter(|_| seat.budget.is_some()) else {
                continue;
            };
            let player = seat.player();
            if self.fallback_logged.contains(&player) {
                continue;
            }
            let reason = fault_kind(fault);
            self.fallback_logged.insert(player);
            self.metrics.brain_fallbacks.fetch_add(1, Ordering::Relaxed);
            let number = self
                .bot_specs
                .iter()
                .find(|(id, _)| *id == player)
                .map_or(0, |(_, spec)| spec.number);
            let line = format!(
                "[bots] match={} seat={number} brain fell back to the fallback bot: {reason}",
                crate::control::room_id_hex(self.id)
            );
            self.bots_log(line);
        }
    }

    /// A bot took (`seated`) or gave up its seat: the director's public lobby listing (P-LOBBY-ACTIVITY-1) shows bot seats.
    pub(super) fn report_bot(&self, name: &str, seated: bool) {
        let _ = self.reports.send(RoomReport::BotSeat {
            match_id: self.id,
            name: name.to_owned(),
            seated,
        });
    }

    /// The newest bot gives its seat up (legacy BotManager.yieldSeat): the fill policy forgets it and the player leaves.
    pub(super) fn yield_newest_bot(&mut self) -> bool {
        let Some((id, name)) = self
            .sim
            .players()
            .iter()
            .rev()
            .find(|player| player.seat.bot)
            .map(|player| (player.id, player.seat.name.clone()))
        else {
            return false;
        };
        if let Some(fill) = self.fill.as_mut() {
            if let Some(spec) = fill.yield_seat() {
                debug_assert_eq!(spec.name, name, "the fill policy seeded the newest bot");
            }
        }
        self.record(ReplayInput::Leave { player: id });
        self.sim.remove_player(id);
        self.report_bot(&name, false);
        self.bots.retain(|seat| seat.player() != id);
        self.bot_specs.retain(|(player, _)| *player != id);
        self.bot_lines.retain(|(player, _)| *player != id);
        true
    }
}
