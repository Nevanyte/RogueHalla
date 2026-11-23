//! The spell executor (P-RAID-S): no goal kinds; it rides along any goal and asks for the cast the rule table names
//! (`spells::SPELL_RULES`). It draws nothing; its one memory is the `CastGate`.
use crate::bots::raid::acts::{Act, CastAim};
use crate::bots::raid::exec::combat::may_fight_from;
use crate::bots::raid::exec::farm::{DEFEND_U, FIGHT_U};
use crate::bots::raid::goal::{Cx, Executor, GoalKind, GoalKind as K, RaidGoal, Step, Target};
use crate::bots::raid::percept::{MobClass, MobSeen};
use crate::bots::raid::spells::CHARGE_RANGE;
use crate::bots::raid::spells::{decide_cast, CastGate, Foe, FoeKind, HeldSpell, SpellScene};
use rh_sim::Vec2;

/// An enemy player this close to the dash's landing point blocks the dash: a melee weapon reaches 72-112 u plus the
/// body radius, so about 130 u hits the seat on arrival; 200 adds a step of approach and stays under the dash's 220 u,
/// so a player a whole dash away never blocks.
const DASH_PLAYER_CLEARANCE_U: f64 = 200.;

fn gap(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// Wire reward id of Cleanse.
const CLEANSE: u8 = 16;
/// `Me::effects` bits: stunned (1), rooted (2).
const STUNNED_OR_ROOTED: u16 = 1 | 2;

/// Casts held quest spells by `spells::SPELL_RULES`.
#[derive(Default)]
pub struct Spell {
    gate: CastGate,
}

fn held(cx: &Cx<'_>) -> Vec<HeldSpell> {
    cx.p.quests.as_ref().map_or_else(Vec::new, |q| {
        q.spells
            .iter()
            .map(|s| HeldSpell {
                slot: s.slot,
                reward: s.reward,
                charges: s.charges,
                ready_at: s.ready_at,
            })
            .collect()
    })
}

/// A living hostile mob (a dummy is no foe, nor is a zombie of the seat's own faction).
fn hostile(m: &MobSeen, cx: &Cx<'_>) -> bool {
    m.alive
        && m.class != MobClass::Dummy
        && !(m.class == MobClass::Zombie && m.faction.is_some() && m.faction == cx.p.me.faction)
}

fn foe_of(m: &MobSeen, cx: &Cx<'_>) -> Foe {
    Foe {
        key: m.key.clone(),
        kind: if m.class == MobClass::Elite {
            FoeKind::Elite
        } else {
            FoeKind::CampMob
        },
        at: m.at,
        distance: cx.p.dist(m.at),
    }
}

fn nearest<'a>(cx: &Cx<'_>, mobs: impl Iterator<Item = &'a MobSeen>) -> Option<&'a MobSeen> {
    mobs.min_by(|a, b| {
        cx.p.dist(a.at)
            .total_cmp(&cx.p.dist(b.at))
            .then_with(|| a.key.cmp(&b.key))
    })
}

/// The foe of the running fight: on a camp (Elite or Farm goal) its elite (the member labelled elite) while one stands,
/// else its nearest member, and only when the seat may fight it from where it stands (`may_fight_from`, the flag of the
/// foe picked) or that foe is engaged within `DEFEND_U` of the seat: a spell cast from outside the camp's band wakes a
/// mob that gives up at the band's edge. On a player fight (Fight goal) that player.
fn foe(goal: &RaidGoal, cx: &Cx<'_>) -> Option<Foe> {
    match (&goal.kind, &goal.target) {
        (K::Elite | K::Farm, Target::Camp(c)) => {
            let members = || {
                cx.p.mobs
                    .iter()
                    .filter(|m| hostile(m, cx) && m.camp == Some(*c))
            };
            let pick = if goal.kind == K::Elite {
                nearest(cx, members().filter(|m| m.class == MobClass::Elite))
                    .or_else(|| nearest(cx, members()))
            } else {
                nearest(cx, members())
            };
            pick.filter(|m| {
                (m.engaged && cx.p.dist(m.at) <= DEFEND_U)
                    || may_fight_from(&*cx.paths, *c, m.class == MobClass::Elite, cx.p.me.at)
            })
            .map(|m| foe_of(m, cx))
        }
        (K::Fight, Target::Actor(key)) => {
            cx.p.players
                .iter()
                .find(|o| o.alive && &o.key == key)
                .map(|o| Foe {
                    key: o.key.clone(),
                    kind: FoeKind::Player,
                    at: o.at,
                    distance: cx.p.dist(o.at),
                })
        }
        _ => None,
    }
}

/// What this think reads of the percept.
fn scene(goal: &RaidGoal, cx: &Cx<'_>) -> SpellScene {
    let me = cx.p.me.at;
    let mut hostile_distances: Vec<f64> =
        cx.p.mobs
            .iter()
            .filter(|m| hostile(m, cx))
            .map(|m| cx.p.dist(m.at))
            .collect();
    hostile_distances.extend(
        cx.p.players
            .iter()
            .filter(|o| o.alive && o.faction != cx.p.me.faction)
            .map(|o| cx.p.dist(o.at)),
    );
    let enemy_player =
        |o: &&crate::bots::raid::percept::PlayerSeen| o.alive && o.faction != cx.p.me.faction;
    // A leave is a fight in reach: a living hostile mob within `FIGHT_U`. Further off the seat is resting.
    let leave_away = (goal.kind == K::Sustain && matches!(goal.target, Target::Camp(_)))
        .then(|| nearest(cx, cx.p.mobs.iter().filter(|m| hostile(m, cx))))
        .flatten()
        .filter(|m| cx.p.dist(m.at) <= FIGHT_U)
        .and_then(|m| {
            let d = cx.p.dist(m.at);
            (d > 1e-6).then(|| Vec2::new((me.x - m.at.x) / d, (me.y - m.at.y) / d))
        })
        .filter(|v| {
            let land = Vec2::new(me.x + v.x * CHARGE_RANGE, me.y + v.y * CHARGE_RANGE);
            !cx.p
                .players
                .iter()
                .filter(enemy_player)
                .any(|o| gap(land, o.at) <= DASH_PLAYER_CLEARANCE_U)
        });
    SpellScene {
        me,
        impaired: cx.p.me.effects & STUNNED_OR_ROOTED != 0,
        foe: foe(goal, cx),
        hostile_distances,
        leave_away,
        hp_share: cx.p.me.hp_share(),
    }
}

/// An npc's key lasts one frame and the act queue waits before it sends, so a cast at a mob names where the mob stands
/// (`CastAim::Mob`); a player's key is stable and stays.
fn stable_aim(act: Act, scene: &SpellScene) -> Act {
    match (act, scene.foe.as_ref()) {
        (
            Act::Cast {
                slot,
                reward,
                aim: CastAim::Actor(key),
            },
            Some(f),
        ) if f.key == key && f.kind != FoeKind::Player => Act::Cast {
            slot,
            reward,
            aim: CastAim::Mob {
                at: f.at,
                elite: f.kind == FoeKind::Elite,
            },
        },
        (act, _) => act,
    }
}

impl Executor for Spell {
    fn kinds(&self) -> &'static [GoalKind] {
        &[]
    }
    fn reset(&mut self) {
        self.gate.reset();
    }
    /// Rooted or stunned with a castable Cleanse: decide now, so the cast is not left to the think cadence.
    fn urgent(&mut self, cx: &mut Cx<'_>) -> bool {
        cx.p.me.effects & STUNNED_OR_ROOTED != 0
            && held(cx)
                .iter()
                .any(|h| h.reward == CLEANSE && h.charges >= 1)
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::none()
    }
    fn ride_along(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let held = held(cx);
        let ask = if held.is_empty() {
            None
        } else {
            let scene = scene(goal, cx);
            decide_cast(&scene, &held, cx.p.now).map(|act| stable_aim(act, &scene))
        };
        let mut step = Step::none();
        if let Some(act) = self.gate.pass(ask, &held, cx.p.now) {
            step.acts.push(act);
            step.why = "cast a quest spell";
        }
        step
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bots::paths::{PathBudget, RoomPathService};
    use crate::bots::perception::LootFacts;
    use crate::bots::planning::PlanMap;
    use crate::bots::raid::exec::combat::may_fight_from;
    use crate::bots::raid::goal::Status;
    use crate::bots::raid::percept::{Me, RaidPercept};
    use crate::bots::raid::values::Prices;
    use crate::bots::random::Rng;
    use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
    use rh_protocol::{WireQuestSpell, WireQuestView};
    use rh_sim::{Arena, FactionId, Vec2};

    const TURRET: u8 = 5;

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

    /// One frame: the seat at `at` holding a Turret, camp 0's elite 300 u east of it.
    fn frame(at: Vec2) -> RaidPercept {
        let base = Percept {
            now: 10_000.,
            hud: HudView {
                phase: "active".into(),
                gates_locked: false,
            },
            self_view: SelfView {
                x: at.x,
                y: at.y,
                status: "alive".into(),
                hp: 100.,
                max_hp: 100.,
                owner_lock: Some(OwnerLock::Clear),
                ..SelfView::default()
            },
            ..Percept::default()
        };
        RaidPercept {
            now: 10_000.,
            tick: 200,
            base,
            facts: LootFacts::default(),
            toasts: Vec::new(),
            self_key: "p1".into(),
            me: Me {
                at,
                hp: 100.,
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
            mobs: vec![MobSeen {
                key: "n3".into(),
                at: Vec2::new(at.x + 300., at.y),
                hp_share: 1.,
                alive: true,
                class: MobClass::Elite,
                mob_type: None,
                weapon: None,
                faction: None,
                camp: Some(0),
                engaged: false,
            }],
            givers: Vec::new(),
            players: Vec::new(),
            quests: Some(WireQuestView {
                spells: vec![WireQuestSpell {
                    slot: 1,
                    reward: TURRET,
                    charges: 1,
                    ready_at: 0.,
                }],
                ..WireQuestView::default()
            }),
            trade: None,
            fields: Vec::new(),
            build: None,
            summary: None,
            arena: None,
            hit_at: None,
        }
    }

    fn asked(at: Vec2) -> Vec<Act> {
        let arena = arena();
        let plan = PlanMap::from_arena(&arena);
        let mut paths = RoomPathService::new(&arena.map, PathBudget::default());
        let prices = Prices::farm_only();
        let mut rng = Rng::new(16);
        let goal = RaidGoal::new(GoalKind::Elite, Target::Camp(0));
        let p = frame(at);
        let mut cx = Cx {
            p: &p,
            paths: &mut paths,
            plan: &plan,
            knowledge: None,
            tier: TierId::Normal,
            persona: PersonaId::Farmer,
            prices: &prices,
            still_since: 0.,
            goal: &goal,
            status: Status::Running,
            rng: &mut rng,
        };
        Spell::default().ride_along(&goal, &mut cx).acts
    }

    /// P-RAID-S-fix2: a Turret is asked for an elite 300 u away only from where camp 0's mob can reach the seat.
    #[test]
    fn turret_asks_nothing_outside_the_band_and_casts_inside_it() {
        let outside = Vec2::new(4000., 4000.);
        let arena = arena();
        let s = &arena.map.layers().expect("layers").data().camps[0].spots[0];
        let inside = Vec2::new(s.x, s.y);
        let paths = RoomPathService::new(&arena.map, PathBudget::default());
        assert!(
            may_fight_from(&paths, 0, true, inside) && !may_fight_from(&paths, 0, true, outside),
            "the spot is inside camp 0's band and (4000, 4000) is not"
        );
        assert!(asked(outside).is_empty(), "outside the band: nothing");
        assert!(
            matches!(
                asked(inside).as_slice(),
                [Act::Cast {
                    reward: TURRET,
                    aim: CastAim::OwnPosition,
                    ..
                }]
            ),
            "inside the band: the Turret"
        );
    }
}
