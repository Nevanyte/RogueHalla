//! Decision-rule tests (P-RAID-Q3, docs/backlog/packets/P-RAID-Q3.md section 8): the pure rules of `decide.rs` (what to do
//! with a pending slot choice, a dropped spell and The Bargain's offers) over `Prices::LOCAL`, the walker's hand-off
//! (it stands aside for a drop only while a slot is free) and the act that sends a pick-up. Table values used below
//! (`Prices::LOCAL` rows, camp-equivalents): watchtowers 0.9 (Scout Falcon, reward 17), barrow_run 1.5 (WallBuster, 20),
//! ember_walk 2.0 (Spell Shield, 9, reusable), a_cruel_joke 2.0 (Swap-Stick, 13, single use), pilgrims_path 2.0 (Cleanse,
//! 16, reusable), letters 2.0 (Forget-Me-Not, 12), smugglers_run 2.5 (Shoulder Charge, 3), hold_the_fort 3.6 (Turret, 5),
//! rime_hunt 3.5 (Tornado, 4), lost_brother 2.5 shared by its two spells (Blind Rage 21, Still Mind 22).
use super::decide::{
    bargain_trade, def_of_reward, pickup_wanted, slot_answer, spell_worth, stat_worth,
};
use super::walker::Walker;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::acts::Act;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::raid::refs::Refs;
use crate::bots::raid::values::Prices;
use crate::bots::random::Rng;
use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{
    WireQuestAction, WireQuestDrop, WireQuestPoint, WireQuestRow, WireQuestSpell, WireQuestView,
};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, PublicCommand, Vec2, WeaponId};

const NOW: f64 = 5_000.;

fn me(weapons: [Option<WeaponId>; 2], slot: u8, faction: FactionId) -> Me {
    Me {
        at: Vec2::new(100., 100.),
        hp: 100.,
        max_hp: 100.,
        alive: true,
        faction: Some(faction),
        weapons,
        slot,
        effects: 0,
        ready_at: 0.,
        potion_small: 0,
        potion_large: 0,
        potions_healing: 0,
        healing_until: None,
    }
}

fn sword() -> Me {
    me([Some(WeaponId::Sword), None], 0, FactionId::Holy)
}

fn held(rewards: &[(u8, u8)]) -> Vec<WireQuestSpell> {
    // (slot, reward id): one charge each.
    rewards
        .iter()
        .map(|&(slot, reward)| WireQuestSpell {
            slot,
            reward,
            charges: 1,
            ready_at: 0.,
        })
        .collect()
}

fn drop_of(reward: u8) -> WireQuestDrop {
    WireQuestDrop {
        index: 0,
        reward,
        charges: 1,
        ready_at: 0.,
        at: WireQuestPoint { x: 100., y: 100. },
    }
}

// ---------------------------------------------------------------------------------------------------------------
// What a spell is worth
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn a_spells_reward_id_maps_back_to_the_quest_that_pays_it() {
    assert_eq!(def_of_reward(9), Some(("ember_walk", 1.)));
    assert_eq!(def_of_reward(20), Some(("barrow_run", 1.)));
    assert_eq!(def_of_reward(16), Some(("pilgrims_path", 1.)));
    // lost_brother pays two spells (Blind Rage and Still Mind): each stands for half its row.
    assert_eq!(def_of_reward(21), Some(("lost_brother", 0.5)));
    assert_eq!(def_of_reward(22), Some(("lost_brother", 0.5)));
    // 23 and up are permanent rewards: no spell carries them.
    assert_eq!(def_of_reward(23), None);
    assert_eq!(def_of_reward(255), None);
}

#[test]
fn a_spells_worth_is_its_quests_table_row_halved_while_a_reusable_one_recharges() {
    let p = &Prices::LOCAL;
    assert_eq!(spell_worth(p, 20, 1), 1.5);
    assert_eq!(spell_worth(p, 9, 1), 2.0);
    assert_eq!(spell_worth(p, 22, 1), 1.25);
    // Cleanse is reusable: with no charge left it is recharging, worth half.
    assert_eq!(spell_worth(p, 16, 0), 1.0);
    // A table that prices quest rewards at zero prices every spell at zero.
    assert_eq!(spell_worth(&Prices::farm_only(), 9, 1), 0.);
}

// ---------------------------------------------------------------------------------------------------------------
// The reward slot choice (action 8: target 0 or 1 drops that slot's spell, 2 leaves the new reward on the ground)
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn the_slot_choice_keeps_the_two_spells_of_higher_value_in_every_order() {
    let p = &Prices::LOCAL;
    // The new reward is the highest (hold_the_fort 3.6): the lower held spell goes, whichever slot holds it.
    assert_eq!(
        slot_answer(p, &held(&[(0, 17), (1, 20)]), "hold_the_fort"),
        0
    );
    assert_eq!(
        slot_answer(p, &held(&[(0, 20), (1, 17)]), "hold_the_fort"),
        1
    );
    // In the middle (ember_walk 2.0 between 0.9 and 2.5): the 0.9 goes.
    assert_eq!(slot_answer(p, &held(&[(0, 3), (1, 17)]), "ember_walk"), 1);
    assert_eq!(slot_answer(p, &held(&[(0, 17), (1, 3)]), "ember_walk"), 0);
    // The lowest (watchtowers 0.9 under 2.5 and 3.5): leave it on the ground.
    assert_eq!(slot_answer(p, &held(&[(0, 3), (1, 4)]), "watchtowers"), 2);
    assert_eq!(slot_answer(p, &held(&[(0, 4), (1, 3)]), "watchtowers"), 2);
}

#[test]
fn the_slot_choice_counts_lost_brothers_spell_at_half_its_row() {
    // Blind Rage is worth 1.25: above a 0.9 spell, below a 1.5 one.
    let p = &Prices::LOCAL;
    assert_eq!(
        slot_answer(p, &held(&[(0, 17), (1, 20)]), "lost_brother"),
        0
    );
    assert_eq!(
        slot_answer(p, &held(&[(0, 20), (1, 20)]), "lost_brother"),
        2
    );
}

#[test]
fn a_tie_goes_to_the_spell_already_held_and_a_single_use_spell_goes_before_a_reusable_one() {
    let p = &Prices::LOCAL;
    // Everything worth 2.0: nothing to gain by swapping, leave the new reward.
    assert_eq!(slot_answer(p, &held(&[(0, 16), (1, 13)]), "ember_walk"), 2);
    // A better new reward (3.6) replaces the held spell of the same worth that is single use (Swap-Stick, 13), not the
    // reusable Cleanse (16): a reusable spell with charges left outranks a single-use one.
    assert_eq!(
        slot_answer(p, &held(&[(0, 13), (1, 16)]), "hold_the_fort"),
        0
    );
    assert_eq!(
        slot_answer(p, &held(&[(0, 16), (1, 13)]), "hold_the_fort"),
        1
    );
}

#[test]
fn a_recharging_reusable_spell_is_worth_half_when_the_slot_is_chosen() {
    let p = &Prices::LOCAL;
    let mut spells = held(&[(0, 16), (1, 13)]);
    spells[0].charges = 0;
    spells[0].ready_at = NOW + 30_000.;
    // Cleanse 2.0 recharging counts 1.0: letters' Forget-Me-Not (2.0) replaces it; watchtowers' Scout Falcon (0.9) does not.
    assert_eq!(slot_answer(p, &spells, "letters"), 0);
    assert_eq!(slot_answer(p, &spells, "watchtowers"), 2);
}

#[test]
fn an_empty_slot_is_filled_before_anything_is_dropped() {
    // Slot 0 empty: the frame offers "Equip in slot 1" as target 0.
    assert_eq!(
        slot_answer(&Prices::LOCAL, &held(&[(1, 3)]), "watchtowers"),
        0
    );
    assert_eq!(
        slot_answer(&Prices::LOCAL, &held(&[(0, 3)]), "watchtowers"),
        1
    );
}

#[test]
fn a_table_that_prices_rewards_at_nothing_leaves_the_new_reward() {
    assert_eq!(
        slot_answer(
            &Prices::farm_only(),
            &held(&[(0, 17), (1, 20)]),
            "hold_the_fort"
        ),
        2
    );
}

// ---------------------------------------------------------------------------------------------------------------
// The pick-up (action 9). The server offers it only while a slot is free (`quest_view/board.rs`: a drop is skipped when
// two spells are held) and refuses the pick-up without one (`rewards/inventory.rs` `pickup`), so a drop worth more than
// a held spell cannot be swapped in: the only way is to answer a pending choice.
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn a_dropped_spell_is_picked_up_when_a_slot_is_free() {
    let p = &Prices::LOCAL;
    assert!(pickup_wanted(p, &held(&[]), &drop_of(17)));
    assert!(pickup_wanted(p, &held(&[(0, 3)]), &drop_of(17)));
}

#[test]
fn a_dropped_spell_is_left_when_both_slots_are_full_whatever_it_is_worth() {
    let p = &Prices::LOCAL;
    assert!(!pickup_wanted(p, &held(&[(0, 3), (1, 4)]), &drop_of(17)));
    assert!(!pickup_wanted(p, &held(&[(0, 17), (1, 20)]), &drop_of(5)));
}

#[test]
fn a_dropped_spell_worth_nothing_is_left_even_with_a_free_slot() {
    assert!(!pickup_wanted(
        &Prices::farm_only(),
        &held(&[]),
        &drop_of(17)
    ));
}

// ---------------------------------------------------------------------------------------------------------------
// The Bargain. A stack trade is `(5, give * 9 + receive)`, the weapon trade `(7, receive)`; stats: 0 damage, 1 move
// speed, 2 max HP, 3 ability power, 4 armor, 5 shield, 6 attack damage, 7 attack speed, 8 lifesteal.
// ---------------------------------------------------------------------------------------------------------------

/// Every stack trade out of `give` (the frame offers one per other stat).
fn out_of(give: u8) -> Vec<(u8, u8)> {
    (0..9)
        .filter(|&j| j != give)
        .map(|j| (5, give * 9 + j))
        .collect()
}

fn weapon_trades() -> Vec<(u8, u8)> {
    (0..9).map(|i| (7, i)).collect()
}

#[test]
fn a_stats_worth_follows_the_weapon_in_hand_and_the_faction() {
    // Neutral stats are worth 2.5 (a chest boost's base, `choice.rs` `card_score`); attack damage and ability power move
    // by the legacy `baseFit`: attack damage +1 for a sword, axe or bow, +0.5 fists, -0.5 wand, else -1; ability power +1
    // wand, +0.5 fists, 0 for a Fire, Sentenced or Plague fighter, else -1; armor and shield +1 for a shield.
    let s = sword();
    assert_eq!(stat_worth(6, &s), 3.5);
    assert_eq!(stat_worth(3, &s), 1.5);
    for neutral in [0, 1, 2, 4, 5, 7, 8] {
        assert_eq!(stat_worth(neutral, &s), 2.5, "stat {neutral}");
    }
    let wand_fire = me([Some(WeaponId::Wand), None], 0, FactionId::Fire);
    assert_eq!(stat_worth(3, &wand_fire), 3.5);
    assert_eq!(stat_worth(6, &wand_fire), 2.0);
    let sword_plague = me([Some(WeaponId::Sword), None], 0, FactionId::Plague);
    assert_eq!(stat_worth(3, &sword_plague), 2.5);
    let guard = me([Some(WeaponId::Shield), None], 0, FactionId::Holy);
    assert_eq!(stat_worth(4, &guard), 3.5);
    assert_eq!(stat_worth(5, &guard), 3.5);
    assert_eq!(stat_worth(6, &guard), 1.5);
    // The weapon in hand is the one in the active slot; an empty slot is fists.
    let bare = me([Some(WeaponId::Sword), None], 1, FactionId::Holy);
    assert_eq!(stat_worth(6, &bare), 3.0);
    assert_eq!(stat_worth(3, &bare), 3.0);
}

#[test]
fn the_spare_weapon_goes_for_the_stat_worth_most_to_the_build() {
    // A sword with a spare axe: attack damage (6) is the best stat.
    let cheap = Prices {
        spare_weapon: 0.75,
        ..Prices::LOCAL
    };
    let got = bargain_trade(&cheap, &sword(), &weapon_trades());
    assert_eq!(got, Some((7, 6)));
}

#[test]
fn stacks_go_for_the_stat_worth_most_to_the_build() {
    // Damage stacks (worth 2.5) into attack damage (3.5) for a sword.
    let got = bargain_trade(&Prices::LOCAL, &sword(), &out_of(0));
    assert_eq!(got, Some((5, 6)));
    // A wand fighter of the Fire faction values ability power most: armor stacks (2.5) go there.
    let wand_fire = me([Some(WeaponId::Wand), None], 0, FactionId::Fire);
    assert_eq!(
        bargain_trade(&Prices::LOCAL, &wand_fire, &out_of(4)),
        Some((5, 4 * 9 + 3))
    );
}

#[test]
fn equal_best_stats_go_to_the_lowest_stat() {
    // A shield values armor (4) and shield (5) the same (3.5): max HP stacks go to armor.
    let guard = me([Some(WeaponId::Shield), None], 0, FactionId::Holy);
    assert_eq!(
        bargain_trade(&Prices::LOCAL, &guard, &out_of(2)),
        Some((5, 2 * 9 + 4))
    );
}

#[test]
fn no_trade_is_made_when_none_improves() {
    // Only ability power is offered for the sword's preferred attack damage. The larger gain still does not pay.
    assert_eq!(
        bargain_trade(&Prices::LOCAL, &sword(), &[(5, 6 * 9 + 3)]),
        None
    );
    // The spare weapon also stays when the only return is ability power.
    assert_eq!(bargain_trade(&Prices::LOCAL, &sword(), &[(7, 3)]), None);
    // Nothing offered, nothing sent.
    assert_eq!(bargain_trade(&Prices::LOCAL, &sword(), &[]), None);
}

#[test]
fn the_weapon_trade_or_the_stack_trade_is_taken_by_what_it_costs() {
    let s = sword();
    // A lot of neutral armor stacks costs less than the usual spare weapon.
    let armor_or_weapon = [(5, 4 * 9 + 6), (7, 6)];
    assert_eq!(
        bargain_trade(&Prices::LOCAL, &s, &armor_or_weapon),
        Some((5, 4 * 9 + 6))
    );
    let cheap_spare = Prices {
        spare_weapon: 0.5,
        ..Prices::LOCAL
    };
    assert_eq!(
        bargain_trade(&cheap_spare, &s, &armor_or_weapon),
        Some((7, 6))
    );
    // Giving ability power stacks (1.5 each, useless to a sword) costs less than the weapon: trade the stacks.
    let power_or_weapon = [(5, 3 * 9 + 6), (7, 6)];
    assert_eq!(
        bargain_trade(&Prices::LOCAL, &s, &power_or_weapon),
        Some((5, 3 * 9 + 6))
    );
}

// ---------------------------------------------------------------------------------------------------------------
// The walker stands aside for a dropped spell only while a slot is free (it is `decide`'s to pick up then); with both
// slots full the drop cannot be picked up and must not freeze the walker (`walker.rs` `drop_near`).
// ---------------------------------------------------------------------------------------------------------------

struct Scene {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
}

fn row(handle: u32, def: &str, giver: Vec2) -> WireQuestRow {
    WireQuestRow {
        handle: QuestHandle::new(handle).expect("a handle"),
        def: def.into(),
        giver: WireQuestPoint {
            x: giver.x,
            y: giver.y,
        },
        state: 1,
        expires_at: 0.,
        participants: Vec::new(),
        carrier: None,
        carrier_actor: None,
        done_by: None,
        can_accept: true,
        owner_accepted: false,
        owner_progress: 0,
        owner_visited: 0,
        name: String::new(),
        giver_label: String::new(),
        brief: String::new(),
        objective: String::new(),
        reward: String::new(),
        owner_completed: false,
        owner_ready: false,
    }
}

/// The owner at camp 0 of the seed1 map in an active raid, holding the view `view` builds.
fn scene(view: impl FnOnce(Vec2) -> WireQuestView) -> Scene {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    let arena = file
        .validate()
        .expect("an admitted map")
        .into_arena()
        .expect("an arena");
    let plan = PlanMap::from_arena(&arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let at = Vec2::new(plan.camps[0].x, plan.camps[0].y);
    let quests = view(at);
    let base = Percept {
        now: NOW,
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
    let mut owner = me([Some(WeaponId::Sword), None], 0, FactionId::Holy);
    owner.at = at;
    let p = RaidPercept {
        now: NOW,
        tick: 100,
        base,
        facts: LootFacts::default(),
        toasts: Vec::new(),
        self_key: "p1".into(),
        me: owner,
        active: true,
        camps: Vec::new(),
        mobs: Vec::new(),
        givers: Vec::new(),
        players: Vec::new(),
        quests: Some(quests),
        trade: None,
        fields: Vec::new(),
        build: None,
        summary: None,
        arena: None,
        hit_at: None,
    };
    Scene { plan, paths, p }
}

fn with_cx<R>(s: &mut Scene, f: impl FnOnce(&mut Cx<'_>) -> R) -> R {
    let goal = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(16);
    let mut cx = Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices: &Prices::LOCAL,
        still_since: 0.,
        goal: &goal,
        status: Status::Running,
        rng: &mut rng,
    };
    f(&mut cx)
}

/// A fresh `tuber_eats` (a permanent reward: it needs no slot) offered at the owner's feet, `n` spells held and a dropped
/// spell at the owner's feet.
/// A wanted drop at the seat's feet. With a slot free the frame offers its pick-up (action 9), as the sim does for a
/// drop in reach: since P-RAID-Q3-fix2 a drop beside the seat that the sim does not offer no longer holds the walker.
fn walker_scene(n: usize) -> Scene {
    scene(|at| {
        let d = drop_of(17);
        let actions = if n < 2 {
            vec![WireQuestAction {
                quest: QuestHandle::new(7).expect("a handle"),
                action: 9,
                target: d.index,
                at: vec![WireQuestPoint { x: at.x, y: at.y }],
                label: "Pick up".into(),
            }]
        } else {
            Vec::new()
        };
        WireQuestView {
            rows: vec![row(1, "tuber_eats", at)],
            spells: held(&[(0, 3), (1, 4)])[..n].to_vec(),
            drops: vec![WireQuestDrop {
                at: WireQuestPoint { x: at.x, y: at.y },
                ..d
            }],
            actions,
            ..WireQuestView::default()
        }
    })
}

fn proposals(s: &mut Scene) -> Vec<Candidate> {
    let mut out = Vec::new();
    with_cx(s, |cx| Walker::default().propose(cx, &mut out));
    out
}

#[test]
fn with_both_slots_full_a_dropped_spell_at_the_feet_does_not_freeze_the_walker() {
    let mut s = walker_scene(2);
    let out = proposals(&mut s);
    assert!(
        out.iter()
            .any(|c| matches!(&c.goal.target, Target::Quest { def, .. } if def == "tuber_eats")),
        "the walker proposes the permanent-reward quest: {out:?}"
    );
    // And it works the quest: it accepts at the giver.
    let goal = RaidGoal::new(
        GoalKind::QuestWalk,
        Target::Quest {
            def: "tuber_eats".into(),
            stage: 0,
        },
    );
    let step = with_cx(&mut s, |cx| Walker::default().act(&goal, cx));
    assert_eq!(step.status, Status::Running, "{step:?}");
    assert!(
        step.acts
            .iter()
            .any(|a| matches!(a, Act::QuestAccept { def } if def == "tuber_eats")),
        "{step:?}"
    );
}

#[test]
fn with_a_slot_free_a_dropped_spell_at_the_feet_still_holds_the_walker_back() {
    // Green today and after: the pick-up comes first while a slot is free.
    for held_spells in [0, 1] {
        let mut s = walker_scene(held_spells);
        assert!(
            proposals(&mut s).is_empty(),
            "{held_spells} spells held: the walker waits for the pick-up"
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------
// The act that sends a pick-up (a drop has no row, so `Act::QuestInteract` cannot name it)
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn a_pickup_act_resolves_to_the_frames_action_9_for_that_drop_and_to_nothing_for_another() {
    let s = scene(|at| WireQuestView {
        spells: held(&[(0, 3)]),
        drops: vec![WireQuestDrop {
            index: 2,
            reward: 17,
            charges: 1,
            ready_at: 0.,
            at: WireQuestPoint { x: at.x, y: at.y },
        }],
        actions: vec![WireQuestAction {
            quest: QuestHandle::new(7).expect("a handle"),
            action: 9,
            target: 2,
            at: vec![WireQuestPoint { x: at.x, y: at.y }],
            label: "Pick up Scout Falcon".into(),
        }],
        ..WireQuestView::default()
    });
    let refs = Refs::build(&s.p, Vec::new());
    let cmd = refs.act(&Act::QuestPickup {
        index: 2,
        reward: 17,
    });
    assert_eq!(
        cmd,
        Some(PublicCommand::QuestInteract {
            handle: rh_sim::quest_view::QuestHandle::new(7).expect("a handle"),
            action: 9,
            target: 2,
        })
    );
    // The index now holds another spell (the list re-sorts by distance every frame): nothing is sent.
    assert_eq!(
        refs.act(&Act::QuestPickup {
            index: 2,
            reward: 5
        }),
        None
    );
    assert_eq!(
        refs.act(&Act::QuestPickup {
            index: 1,
            reward: 17
        }),
        None
    );
}
