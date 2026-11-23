//! P-RAID-HOLD-1 oracle (docs/backlog/packets/P-RAID-HOLD-1.md): a seat "healing up" (a leave's stand, or the plain
//! heal-up candidate) stops standing when something is hurting it, by any source, or when regeneration cannot happen
//! (locked gates); it still stands and heals when nothing hurts it.
//!
//! The scene is the executor itself, driven frame by frame as the seat drives it (`start`, then `observe` and `act` or
//! `propose` per frame) on hand-built percepts: no mob is in view, so the hold's own view (`nearest`, regional rows
//! only) sees nothing, as it does for another player's shot, a damage-over-time effect or an elite. A whole-sim scene
//! was tried first and cannot isolate this: the base seat leaves a heal-up through other goals whenever a player or a
//! camp comes near, and a player attacker kills a half-hp seat in a few hits.
use rh_policy::bots::paths::{PathBudget, RoomPathService};
use rh_policy::bots::perception::LootFacts;
use rh_policy::bots::planning::PlanMap;
use rh_policy::bots::raid::exec::sustain::Sustain;
use rh_policy::bots::raid::goal::{
    Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target,
};
use rh_policy::bots::raid::percept::{Me, RaidPercept};
use rh_policy::bots::raid::values::{Prices, SustainPrices};
use rh_policy::bots::random::Rng;
use rh_policy::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_sim::{Arena, FactionId};

const FRAME_MS: f64 = 50.;
/// A stand must end within this many frames (1 s) of the first sign of harm.
const REACT_FRAMES: usize = 20;

fn arena() -> Arena {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    file.validate()
        .expect("admitted")
        .into_arena()
        .expect("an arena")
}

struct World {
    arena: Arena,
    plan: PlanMap,
    paths: RoomPathService,
    exec: Sustain,
    rng: Rng,
    prices: Prices,
}

impl World {
    fn new(sustain: SustainPrices) -> Self {
        let arena = arena();
        let plan = PlanMap::from_arena(&arena);
        let paths = RoomPathService::new(&arena.map, PathBudget::default());
        World {
            arena,
            plan,
            paths,
            exec: Sustain::default(),
            rng: Rng::new(16),
            prices: Prices {
                sustain,
                ..Prices::farm_only()
            },
        }
    }

    /// Frame `n` (server ms `n * 50`): hp share, a hit landed this frame or not, gates locked or not. No mob, no camp,
    /// no other player in view.
    fn frame(&self, n: usize, hp: f64, hit: bool, locked: bool) -> RaidPercept {
        let now = n as f64 * FRAME_MS;
        let at = self.arena.centre();
        let base = Percept {
            now,
            hud: HudView {
                phase: "active".into(),
                gates_locked: locked,
            },
            self_view: SelfView {
                x: at.x,
                y: at.y,
                status: "alive".into(),
                hp: 100. * hp,
                max_hp: 100.,
                owner_lock: Some(OwnerLock::Clear),
                ..SelfView::default()
            },
            ..Percept::default()
        };
        RaidPercept {
            now,
            tick: n as u32,
            base,
            facts: LootFacts::default(),
            toasts: Vec::new(),
            self_key: "p1".into(),
            me: Me {
                at,
                hp: 100. * hp,
                max_hp: 100.,
                alive: true,
                faction: Some(FactionId::Holy),
                weapons: [None, None],
                slot: 0,
                effects: 0,
                ready_at: 0.,
                potion_small: 0,
                potion_large: 0,
                potions_healing: 0,
                healing_until: None,
            },
            active: true,
            camps: Vec::new(),
            mobs: Vec::new(),
            givers: Vec::new(),
            players: Vec::new(),
            quests: None,
            trade: None,
            fields: Vec::new(),
            build: None,
            summary: None,
            arena: None,
            hit_at: hit.then_some(now),
        }
    }

    /// One frame as the seat runs it with `goal` held: `observe`, then `act` (the hold's think).
    fn act(&mut self, p: &RaidPercept, goal: &RaidGoal) -> Step {
        let mut cx = Cx {
            p,
            paths: &mut self.paths,
            plan: &self.plan,
            knowledge: None,
            tier: TierId::Normal,
            persona: PersonaId::Farmer,
            prices: &self.prices,
            still_since: 0.,
            goal,
            status: Status::Running,
            rng: &mut self.rng,
        };
        self.exec.observe(&mut cx);
        self.exec.act(goal, &mut cx)
    }

    fn start(&mut self, p: &RaidPercept, goal: &RaidGoal) {
        let mut cx = Cx {
            p,
            paths: &mut self.paths,
            plan: &self.plan,
            knowledge: None,
            tier: TierId::Normal,
            persona: PersonaId::Farmer,
            prices: &self.prices,
            still_since: 0.,
            goal,
            status: Status::Running,
            rng: &mut self.rng,
        };
        self.exec.start(goal, &mut cx);
    }

    /// The candidates the executor offers at this frame while the seat holds `goal`.
    fn propose(&mut self, p: &RaidPercept, goal: &RaidGoal) -> Vec<Candidate> {
        let mut cx = Cx {
            p,
            paths: &mut self.paths,
            plan: &self.plan,
            knowledge: None,
            tier: TierId::Normal,
            persona: PersonaId::Farmer,
            prices: &self.prices,
            still_since: 0.,
            goal,
            status: Status::Running,
            rng: &mut self.rng,
        };
        let mut out = Vec::new();
        self.exec.observe(&mut cx);
        self.exec.propose(&mut cx, &mut out);
        out
    }
}

fn heal_up() -> RaidGoal {
    RaidGoal::new(GoalKind::Sustain, Target::None)
}

fn roam() -> RaidGoal {
    RaidGoal::new(GoalKind::Roam, Target::None)
}

fn standing(s: &Step) -> bool {
    s.status == Status::Running && s.intents.is_empty() && s.acts.is_empty()
}

fn offers_heal_up(out: &[Candidate]) -> bool {
    out.iter()
        .any(|c| c.goal.kind == GoalKind::Sustain && matches!(c.goal.target, Target::None))
}

/// Ten quiet frames of a stand at hp 0.5, then hp falls 0.01 a frame (with a hit every 5th frame when `hits`). Returns
/// the number of frames after the first fall at which the seat is no longer standing.
fn first_not_standing(w: &mut World, hits: bool) -> Option<usize> {
    let goal = heal_up();
    let p0 = w.frame(0, 0.5, false, false);
    w.start(&p0, &goal);
    for n in 0..10 {
        let s = w.act(&w.frame(n, 0.5, false, false), &goal);
        assert!(standing(&s), "the quiet stand stands at frame {n}: {s:?}");
    }
    let mut hp = 0.5;
    for k in 0..60 {
        hp -= 0.01;
        let hit = hits && k % 5 == 0;
        let s = w.act(&w.frame(10 + k, hp, hit, false), &goal);
        if !standing(&s) {
            return Some(k);
        }
    }
    None
}

#[test]
fn a_stand_ends_when_hp_falls_from_a_source_the_hold_does_not_see() {
    // No hit event, hp simply falls (a damage-over-time effect, an area, an elite out of view).
    let mut w = World::new(SustainPrices::LOCAL);
    let k = first_not_standing(&mut w, false);
    assert!(
        k.is_some_and(|k| k < REACT_FRAMES),
        "hp fell 0.01 a frame for 60 frames and the stand ended at {k:?} (bound {REACT_FRAMES} frames)"
    );
}

#[test]
fn a_stand_ends_when_the_seat_is_hit() {
    let mut w = World::new(SustainPrices::LOCAL);
    let k = first_not_standing(&mut w, true);
    assert!(
        k.is_some_and(|k| k < REACT_FRAMES),
        "hit every 5th frame with hp falling and the stand ended at {k:?} (bound {REACT_FRAMES} frames)"
    );
}

#[test]
fn a_heal_up_is_not_proposed_while_something_hurts_the_seat() {
    let mut w = World::new(SustainPrices::LOCAL);
    let mut hp = 0.6;
    let mut offered = 0;
    for n in 0..40 {
        hp -= 0.01;
        let out = w.propose(&w.frame(n, hp, n % 5 == 0, false), &roam());
        if n >= REACT_FRAMES && offers_heal_up(&out) {
            offered += 1;
        }
    }
    assert_eq!(
        offered, 0,
        "a heal-up was offered {offered} of the last 20 frames while hp fell and hits landed"
    );
}

#[test]
fn a_stand_ends_when_the_gates_are_locked() {
    // A locked coliseum stops all regeneration (`systems/regen.rs`): standing there can end only in death.
    let mut w = World::new(SustainPrices::LOCAL);
    let goal = heal_up();
    let p0 = w.frame(0, 0.5, false, true);
    w.start(&p0, &goal);
    let s = w.act(&p0, &goal);
    assert!(
        !standing(&s),
        "locked gates, no regeneration, and the seat stands: {s:?}"
    );
    let out = w.propose(&w.frame(1, 0.5, false, true), &roam());
    assert!(
        !offers_heal_up(&out),
        "a heal-up is offered while the gates are locked: {out:?}"
    );
}

#[test]
fn guard_a_quiet_stand_still_stands_heals_and_ends_at_ready_hp() {
    let mut w = World::new(SustainPrices::LOCAL);
    let goal = heal_up();
    let p0 = w.frame(0, 0.5, false, false);
    w.start(&p0, &goal);
    let out = w.propose(&p0, &roam());
    assert!(
        offers_heal_up(&out),
        "a quiet seat at hp 0.5 proposes the heal-up: {out:?}"
    );
    // 0.8 % of max hp a second: 0.0004 a frame.
    let mut hp = 0.5;
    for n in 0..400 {
        let s = w.act(&w.frame(n, hp, false, false), &goal);
        assert!(
            standing(&s),
            "frame {n}, hp {hp}: a quiet seat keeps standing: {s:?}"
        );
        hp += 0.0004;
    }
    let done = w.act(&w.frame(400, 0.86, false, false), &goal);
    assert_eq!(
        done.status,
        Status::Done,
        "at ready_hp the hold ends: {done:?}"
    );
}

#[test]
fn guard_a_hit_longer_ago_than_hurt_seconds_does_not_end_the_stand() {
    let mut w = World::new(SustainPrices::LOCAL);
    let goal = heal_up();
    w.start(&w.frame(0, 0.4, false, false), &goal);
    let _ = w.act(&w.frame(0, 0.4, true, false), &goal);
    // hp steady: from 3.5 s on after the hit (70 frames) the stand holds again.
    for n in 1..200 {
        let s = w.act(&w.frame(n, 0.4, false, false), &goal);
        if n > 70 {
            assert!(
                standing(&s),
                "frame {n}, 3.5 s after the hit, hp steady: {s:?}"
            );
        }
    }
}

#[test]
fn guard_the_hold_state_ends_with_its_goal_and_the_rng_is_untouched() {
    let mut w = World::new(SustainPrices::LOCAL);
    let before = w.rng.clone();
    let k = first_not_standing(&mut w, true);
    assert!(k.is_some(), "setup: the stand ended under fire");
    // Another goal holds the seat for a frame (the hold's state must go with its goal), then a fresh quiet stand.
    let _ = w.propose(&w.frame(200, 0.3, false, false), &roam());
    let goal = heal_up();
    w.start(&w.frame(300, 0.3, false, false), &goal);
    for n in 300..330 {
        let s = w.act(&w.frame(n, 0.3, false, false), &goal);
        assert!(standing(&s), "frame {n}: a fresh quiet stand stands: {s:?}");
    }
    assert_eq!(w.rng, before, "the executor drew from the rng");
}

#[test]
fn guard_sustain_off_proposes_nothing_and_never_acts() {
    let mut w = World::new(SustainPrices::OFF);
    let goal = heal_up();
    let p = w.frame(0, 0.3, true, true);
    assert!(w.propose(&p, &goal).is_empty());
    assert_eq!(w.act(&p, &goal).status, Status::Failed);
}
