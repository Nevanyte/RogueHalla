//! The RaidBot policy seat (P-RAID-0): checks its input as HumanBot does, answers the planning screen through
//! `PlanModule`, builds the percept, runs the arbiter and the executors, and emits through the Humanizer and the act
//! queue, inside the seat's order budget. Streams: `derive(seed, 0/1/2/3)` decide, motor, lapse and Knowledge as
//! HumanBot; 16 the arbiter and executors; 17 the act queue.
use super::acts::{ActQueue, OrderBudgetMirror};
use super::arbiter::{Arbiter, Prices};
use super::exec;
use super::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use super::percept::RaidPercept;
use super::refs::Refs;
use crate::bots::bot::emit_command;
use crate::bots::brain::merge_intents;
use crate::bots::clicks::BotMap;
use crate::bots::delivered::{Epoch, PlayerRef};
use crate::bots::humanizer::{Context, Humanizer};
use crate::bots::knowledge::Knowledge;
use crate::bots::memory_prefix::{Memory, OwnerSight, Scene, MAX_RECORDS};
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::Perception;
use crate::bots::planning::{PlanDeal, PlanLearned, PlanMap, PlanModule};
use crate::bots::random::{derive, uniform, Rng};
use crate::bots::types::{
    Intent, OwnerLock, Percept, PersonaId, PersonaParams, StimulusKind, TierId, TierParams,
};
use crate::delivery::Frame14;
use crate::{
    InputKind, InputPreview, InputRefusal, MatchStart, Policy, PolicyInput, PolicySpec,
    POLICY_CONTRACT_VERSION,
};
use rh_sim::{Observation, PlayerId, PublicCommand, OBSERVATION_VERSION};

/// The arbiter's stream tag (the executors draw from it too, in registry order).
pub const ARBITER_TAG: u32 = 16;
/// The act queue's stream tag.
pub const ACTS_TAG: u32 = 17;

/// The balance lab's test player on one seat. One frame, in order: memory scene -> (planning screen: `PlanModule`,
/// done) -> shared hand percept -> `RaidPercept` -> `Refs` -> path service tick -> every executor's `observe` -> on a
/// strategy tick every executor's `propose` and the arbiter's choice -> on a think tick (every frame while committed)
/// the goal's executor `act` plus every executor's `ride_along` -> Humanizer -> commands resolved through `Refs` and
/// admitted by the budget mirror -> the act queue.
pub struct RaidBot {
    tier: TierId,
    persona: PersonaId,
    prices: Prices,
    me: Option<PlayerId>,
    humanizer: Option<Humanizer>,
    perception: Perception,
    memory: Option<Memory>,
    paths: Option<RoomPathService>,
    planning: PlanModule,
    plan_map: PlanMap,
    plan_learned: PlanLearned,
    knowledge: Option<Knowledge>,
    decide: Rng,
    motor: Rng,
    lapse: Rng,
    arbiter_rng: Rng,
    arbiter: Arbiter,
    executors: Vec<Box<dyn Executor>>,
    goal: RaidGoal,
    status: Status,
    committed: bool,
    why: &'static str,
    next_think_at: f64,
    acts: ActQueue,
    budget: OrderBudgetMirror,
    /// Commands the budget mirror held back (the sim would have refused them).
    held_back: u64,
    low_hp_prev: f64,
}

/// The goal before the first strategy tick: nothing (it reads as finished, so the first tick decides).
fn no_goal() -> RaidGoal {
    RaidGoal::new(GoalKind::Roam, Target::None)
}

impl RaidBot {
    pub fn new(tier: TierId, persona: PersonaId) -> Self {
        Self {
            tier,
            persona,
            prices: Prices::LOCAL,
            me: None,
            humanizer: None,
            perception: Perception::default(),
            memory: None,
            paths: None,
            planning: PlanModule::new(),
            plan_map: PlanMap::default(),
            plan_learned: PlanLearned::default(),
            knowledge: None,
            decide: Rng::new(1),
            motor: Rng::new(2),
            lapse: Rng::new(3),
            arbiter_rng: Rng::new(ARBITER_TAG),
            arbiter: Arbiter::default(),
            executors: exec::registry(),
            goal: no_goal(),
            status: Status::Done,
            committed: false,
            why: "",
            next_think_at: 0.,
            acts: ActQueue::default(),
            budget: OrderBudgetMirror::default(),
            held_back: 0,
            low_hp_prev: 1.,
        }
    }
    /// The same seat pricing goals with `prices` (P-RAID-V's table, or a lab experiment).
    pub fn with_prices(mut self, prices: Prices) -> Self {
        self.prices = prices;
        self
    }
    pub fn tier(&self) -> TierId {
        self.tier
    }
    pub fn persona(&self) -> PersonaId {
        self.persona
    }
    /// The goal the seat holds and what its executor last said.
    pub fn goal(&self) -> (&RaidGoal, Status, &'static str) {
        (&self.goal, self.status, self.why)
    }
    /// The last strategy tick's ranking.
    pub fn arbiter(&self) -> &Arbiter {
        &self.arbiter
    }
    /// The act queue's counters and the commands the budget mirror held back.
    pub fn act_stats(&self) -> (super::acts::ActStats, u64) {
        (self.acts.stats(), self.held_back)
    }
}

impl Policy for RaidBot {
    fn input_requirements(&self) -> crate::InputRequirements {
        crate::InputRequirements {
            inventory: true,
            owner_combat: true,
        }
    }
    fn input_kind(&self) -> InputKind {
        InputKind::Delivered14
    }
    fn spec(&self) -> PolicySpec {
        PolicySpec {
            name: format!("raid-{}-{}", self.tier.as_str(), self.persona.as_str()),
            version: "0".into(),
            contract: POLICY_CONTRACT_VERSION,
            observation_version: OBSERVATION_VERSION,
            decision_interval_ticks: 1,
        }
    }
    fn stats(&self) -> Option<crate::PolicyStats> {
        let s = self.humanizer.as_ref()?.stats();
        Some(crate::PolicyStats {
            commands: crate::bots::types::COMMAND_TYPES
                .iter()
                .map(|t| (t.as_str().to_owned(), s.command(*t)))
                .collect(),
            reactions: s
                .reactions
                .iter()
                .map(|(k, v)| (k.as_str().to_owned(), v.clone()))
                .collect(),
            target_clicks: s.target_clicks,
            click_missed: s.click_missed,
            click_wrong: s.click_wrong,
            hesitations: s.hesitations,
            lapses: s.lapses,
            throttled: s.throttled,
            superseded: s.superseded,
            apm_peak: s.apm_peak,
        })
    }
    fn reset(&mut self, m: &MatchStart) {
        // HumanBot's streams for the shared modules (bots/bot.rs reset): derive(seed, 0/1/2) decide/motor/lapse, 3 for
        // Knowledge; the RaidBot's own decisions draw from 16 (arbiter) and 17 (act queue).
        let seed = m.seed as u32;
        self.decide = Rng::new(derive(seed, 0));
        self.motor = Rng::new(derive(seed, 1));
        self.lapse = Rng::new(derive(seed, 2));
        self.arbiter_rng = Rng::new(derive(seed, ARBITER_TAG));
        self.acts = ActQueue::new(Rng::new(derive(seed, ACTS_TAG)));
        let map = BotMap::from_tile_map(&m.arena.map);
        self.humanizer = Some(Humanizer::new(TierParams::by_id(self.tier), map));
        self.perception.reset();
        self.me = Some(m.me);
        self.memory = PlayerRef::new(m.me.0)
            .ok()
            .and_then(|viewer| Memory::new(Epoch(1), viewer, self.tier, MAX_RECORDS).ok());
        // The sim's own arena (after any map admission swap): the plan map and paths are read from it, never a file.
        self.paths = Some(RoomPathService::new(&m.arena.map, PathBudget::default()));
        self.planning.reset();
        self.plan_map = PlanMap::from_arena(&m.arena);
        self.plan_learned = PlanLearned::default();
        self.knowledge = Some(
            Knowledge::create(TierParams::by_id(self.tier), &mut Rng::new(derive(seed, 3)))
                .with_elite_rule(m.elites),
        );
        self.arbiter.reset();
        for e in &mut self.executors {
            e.reset();
        }
        self.goal = no_goal();
        self.status = Status::Done;
        self.committed = false;
        self.why = "";
        self.next_think_at = 0.;
        self.budget = OrderBudgetMirror::default();
        self.held_back = 0;
        self.low_hp_prev = 1.;
    }
    fn decide(&mut self, obs: &Observation) -> Vec<PublicCommand> {
        let mut out = Vec::new();
        self.decide_into(obs, &mut out);
        out
    }
    // A raw Observation is refused by `check_input` before any mutation; this callback has nothing to do.
    fn decide_into(&mut self, _: &Observation, _: &mut Vec<PublicCommand>) {}
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        let InputPreview::Delivered14(frame) = input else {
            return Err(InputRefusal::RequiresDelivered14);
        };
        self.check_frame(frame)
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        out: &mut Vec<PublicCommand>,
    ) -> Result<(), InputRefusal> {
        self.check_input(input.preview())?;
        let PolicyInput::Delivered14(accepted) = input else {
            unreachable!("checked above")
        };
        self.decide_frame_into(accepted.frame(), out);
        Ok(())
    }
}

/// Borrows of the seat an executor call needs (disjoint from the executor list).
struct Parts<'a> {
    paths: &'a mut RoomPathService,
    plan: &'a PlanMap,
    knowledge: Option<&'a Knowledge>,
    prices: &'a Prices,
    rng: &'a mut Rng,
    tier: TierId,
    persona: PersonaId,
    still_since: f64,
}

impl<'a> Parts<'a> {
    fn cx<'b>(&'b mut self, p: &'b RaidPercept, goal: &'b RaidGoal, status: Status) -> Cx<'b> {
        Cx {
            p,
            paths: &mut *self.paths,
            plan: self.plan,
            knowledge: self.knowledge,
            tier: self.tier,
            persona: self.persona,
            prices: self.prices,
            still_since: self.still_since,
            goal,
            status,
            rng: &mut *self.rng,
        }
    }
}

impl RaidBot {
    /// HumanBot's input checks (`bots/bot.rs` check_frame): viewer and owner match, inventory and owner combat present
    /// outside planning, and the memory accepts the scene.
    fn check_frame(&self, obs: &Frame14) -> Result<(), InputRefusal> {
        if Some(PlayerId(obs.context().viewer.get())) != self.me {
            return Err(InputRefusal::InvalidOwner);
        }
        let Some(owner) = obs.owner.as_ref() else {
            return Ok(());
        };
        if Some(PlayerId(owner.id)) != self.me {
            return Err(InputRefusal::InvalidOwner);
        }
        if obs.phase != 3 {
            if obs.collections.inventory.is_none() {
                return Err(InputRefusal::InventoryUnavailable);
            }
            if obs.owner_combat.is_none() {
                return Err(InputRefusal::OwnerCombatUnavailable);
            }
        }
        let players = crate::bots::perception::memory_players(obs)
            .map_err(|_| InputRefusal::MemoryUnavailable)?;
        let viewer = PlayerRef::new(owner.id).map_err(|_| InputRefusal::InvalidOwner)?;
        self.memory
            .as_ref()
            .ok_or(InputRefusal::MemoryUnavailable)?
            .check_scene(Scene {
                epoch: obs.context().epoch,
                viewer,
                now: obs.source_time,
                owner: Some(OwnerSight {
                    at: rh_sim::Vec2::new(f64::from(owner.x), f64::from(owner.y)),
                    status: crate::delivery::frame::status(owner.status),
                }),
                players: &players,
            })
            .map_err(|_| InputRefusal::ClockResetRequired)
    }

    /// The planning screen: the module reads the private deal and answers it (`bots/bot.rs` planning branch).
    fn plan_into(&mut self, obs: &Frame14, out: &mut Vec<PublicCommand>) {
        let now = obs.source_time;
        let deal = obs.collections.plan.as_ref().map(|view| PlanDeal {
            ends_at: view.ends_at,
            spawns: view.spawns.map(|v| v.map_or(-1, |n| n as i32)),
            taken: view.taken,
            pick: view.pick.map_or(-1, |n| n as i8),
            boons: view.boons.clone(),
            boon: view.boon.map_or(-1, |n| n as i8),
        });
        let faction = obs
            .owner
            .as_ref()
            .and_then(|me| rh_sim::FactionId::ALL.get(me.faction as usize).copied());
        let tier = self.tier;
        self.planning
            .observe(now, deal.as_ref(), tier, &mut self.decide);
        let intent = self.planning.intents(
            now,
            true,
            tier,
            &self.plan_map,
            faction,
            None,
            &self.plan_learned,
            &mut self.decide,
            &mut self.lapse,
        );
        if let Some(intent) = intent {
            // A plan click spends an order token like any command.
            if !self.budget.allow(now) {
                self.held_back += 1;
                return;
            }
            out.push(PublicCommand::Plan {
                spawn: intent.spawn,
                boon: intent.boon,
                ready: intent.ready,
            });
        }
    }

    fn decide_frame_into(&mut self, obs: &Frame14, out: &mut Vec<PublicCommand>) {
        let Some(owner) = obs.owner.as_ref() else {
            return;
        };
        let Ok(players) = crate::bots::perception::memory_players(obs) else {
            return;
        };
        let Ok(viewer) = PlayerRef::new(owner.id) else {
            return;
        };
        let Some(memory) = self.memory.as_mut() else {
            return;
        };
        let scene = Scene {
            epoch: obs.context().epoch,
            viewer,
            now: obs.source_time,
            owner: Some(OwnerSight {
                at: rh_sim::Vec2::new(f64::from(owner.x), f64::from(owner.y)),
                status: crate::delivery::frame::status(owner.status),
            }),
            players: &players,
        };
        if memory.observe_scene(scene).is_err() {
            return;
        }
        let urgent_memory = memory.stimuli().iter().any(|s| {
            matches!(
                s.kind,
                StimulusKind::NewThreat | StimulusKind::Teleport | StimulusKind::LockLost
            )
        });
        let still_since = memory
            .own_motion()
            .expect("accepted owner scene")
            .still_since;
        if obs.phase == 3 {
            self.plan_into(obs, out);
            return;
        }
        let tier = TierParams::by_id(self.tier);
        let Some(frame) = self.perception.build(obs, tier.attention.central) else {
            return;
        };
        let Some(p) =
            RaidPercept::build(obs, frame.percept, frame.loot, frame.hit_at, &self.plan_map)
        else {
            return;
        };
        let refs = Refs::build(&p, frame.ids);
        let hp = p.me.hp_share();
        let low_hp = self.low_hp_prev >= 0.5 && hp < 0.5;
        self.low_hp_prev = hp;
        if let Some(paths) = self.paths.as_mut() {
            paths.apply_terrain(obs.collections().destroyed_terrain());
            paths.budget_mut().start_tick();
            paths.set_frame(p.now, p.base.hud.gates_locked);
        }
        let (intents, acts) = self.think(&p, still_since, urgent_memory || low_hp);
        self.release(&p.base, &intents, &refs, out);
        for act in acts {
            self.acts.push(act, p.now);
        }
        self.acts.release(p.now, &refs, &mut self.budget, out);
    }

    /// Observe, decide and act for one frame: the intents for the Humanizer and the acts for the queue.
    fn think(
        &mut self,
        p: &RaidPercept,
        still_since: f64,
        urgent: bool,
    ) -> (Vec<Intent>, Vec<super::acts::Act>) {
        let Some(paths) = self.paths.as_mut() else {
            return (Vec::new(), Vec::new());
        };
        let now = p.now;
        let mut parts = Parts {
            paths,
            plan: &self.plan_map,
            knowledge: self.knowledge.as_ref(),
            prices: &self.prices,
            rng: &mut self.arbiter_rng,
            tier: self.tier,
            persona: self.persona,
            still_since,
        };
        let held = self.goal.clone();
        for e in &mut self.executors {
            e.observe(&mut parts.cx(p, &held, self.status));
        }
        if !p.active {
            return (Vec::new(), Vec::new());
        }
        let mut changed = false;
        if self.arbiter.due(now, self.status, urgent) {
            let mut candidates: Vec<Candidate> = Vec::new();
            for e in &mut self.executors {
                e.propose(&mut parts.cx(p, &held, self.status), &mut candidates);
            }
            let tier = TierParams::by_id(self.tier);
            let persona = PersonaParams::by_id(self.persona);
            let chosen = self.arbiter.choose(
                now,
                candidates,
                (&held, self.status, self.committed),
                tier,
                persona,
                parts.rng,
            );
            let next = chosen.unwrap_or_else(no_goal);
            if !next.same(&held) || self.status != Status::Running {
                changed = true;
                self.goal = next;
                self.status = Status::Running;
                self.committed = false;
                let goal = self.goal.clone();
                if let Some(e) = self
                    .executors
                    .iter_mut()
                    .find(|e| e.kinds().contains(&goal.kind))
                {
                    e.start(&goal, &mut parts.cx(p, &goal, Status::Running));
                }
            } else {
                // The same goal, possibly with a fresher target payload (a moved point): keep the executor's state.
                self.goal = next;
            }
        }
        // A committed goal (a fight) thinks every frame: a melee lock needs this frame's positions, as HumanBot's front end
        // re-plans every frame. Others think on the tier's cadence.
        if !(changed || urgent || self.committed || now >= self.next_think_at) {
            return (Vec::new(), Vec::new());
        }
        let goal = self.goal.clone();
        let step = match self
            .executors
            .iter_mut()
            .find(|e| e.kinds().contains(&goal.kind))
        {
            Some(e) => e.act(&goal, &mut parts.cx(p, &goal, self.status)),
            None => Step::failed("no executor for the goal"),
        };
        self.status = step.status;
        self.committed = step.committed;
        self.why = step.why;
        let mut intents = step.intents;
        let mut acts = step.acts;
        for e in &mut self.executors {
            let extra = e.ride_along(&goal, &mut parts.cx(p, &goal, self.status));
            intents = merge_intents(intents, extra.intents);
            acts.extend(extra.acts);
        }
        let think = &TierParams::by_id(self.tier).think_ms;
        self.next_think_at = now
            + uniform(
                parts.rng,
                think.mean - think.jitter,
                think.mean + think.jitter,
            )
            .min(2000.);
        (intents, acts)
    }

    /// Hand the intents to the Humanizer and emit what it releases, each command resolved against this frame's
    /// references and admitted by the order-budget mirror.
    fn release(
        &mut self,
        base: &Percept,
        intents: &[Intent],
        refs: &Refs,
        out: &mut Vec<PublicCommand>,
    ) {
        let Some(humanizer) = self.humanizer.as_mut() else {
            return;
        };
        let engaged = matches!(base.self_view.owner_lock, Some(OwnerLock::Held { .. }));
        let cmds = {
            let mut ctx = Context {
                p: base,
                engaged,
                motor: &mut self.motor,
                lapse: &mut self.lapse,
            };
            humanizer.submit(intents, &mut ctx);
            humanizer.release(&mut ctx)
        };
        // Click misses are a stimulus for HumanBot's brain cadence; this seat re-thinks on its own cadence.
        drop(humanizer.drain_misses());
        let mut one = Vec::with_capacity(1);
        for timed in cmds {
            one.clear();
            emit_command(timed.cmd.clone(), None, refs, &mut one);
            let Some(cmd) = one.pop() else {
                continue;
            };
            if !self.budget.allow(base.now) {
                self.held_back += 1;
                continue;
            }
            out.push(cmd);
            if let Some(memory) = self.memory.as_mut() {
                memory.note_sent(&timed);
            }
        }
    }
}
