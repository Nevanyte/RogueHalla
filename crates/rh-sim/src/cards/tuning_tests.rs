//! P-AUG-RULES-1 oracle (in-crate half). The integration half is `tests/augment_rules.rs`.
//!
//! The rework override table is empty in this packet, so a test that a reader goes THROUGH the table needs a table of
//! its own: `Tuning::for_test` builds one (through the shape guard), and the test puts it on `sim.k.cards`. The doctored
//! numbers below are test values, not rework numbers: what is pinned is that each reader reads the table.
use super::{check_override, CardNumbers, Tuning};
use crate::cards::{
    BaseStatId, CardDef, CardId, CardKind, CardRow, MechanicTag, Selection, TriggerAction,
    TriggerOn, TriggerRow,
};
use crate::modifiers::{OutgoingKind, VsProbe};
use crate::stat_fold::{EffectFlag, EffectKind, EffectStat, MultKey};
use crate::{arenas, AugmentRule, FactionId, Placement, PlayerId, Secrets, Sim, SimConfig, Vec2};

/// The legacy numbers, written from the constants (not read from the stub).
fn legacy_numbers() -> CardNumbers {
    use crate::constants as c;
    CardNumbers {
        plant_feet_bonus: c::PLANT_FEET_BONUS,
        gambler_up_chance: c::GAMBLER_CHANCE,
        gambler_down_chance: c::GAMBLER_CHANCE,
        gambler_up: c::GAMBLER_UP,
        gambler_down: c::GAMBLER_DOWN,
        wishful_budget_ms: c::WISHFUL_BUDGET_MS,
        wishful_window_ms: c::WISHFUL_WINDOW_MS,
        wishful_floor_share: c::WISHFUL_FLOOR_SHARE,
        outgoing_row_cap: c::OUTGOING_ROW_CAP,
        hp_race_cap: c::HP_RACE_CAP,
        third_time_every: 3,
        third_time_extra: 1.,
    }
}

const COMMITTED_ROWS_22: &[CardRow] = &[
    CardRow::Fold(EffectKind::Flag {
        flag: EffectFlag::LockSecondSlot,
    }),
    CardRow::Fold(EffectKind::Stat {
        stat: EffectStat::Damage,
        v: 0.22,
    }),
    CardRow::Fold(EffectKind::Stat {
        stat: EffectStat::AttackSpeed,
        v: 0.15,
    }),
];

/// Committed with the Augment_Tuning damage (+22%) and its text: a legal override (row number and text only).
const COMMITTED_22: CardDef = CardDef {
    id: CardId::Committed,
    kind: CardKind::Augment,
    tier: 3,
    base: None,
    requires: None,
    unique: true,
    ready: true,
    title: "Committed",
    text: "Your second weapon slot is locked. The weapon you hold deals +22% damage and attacks 15% faster.",
    tags: &[],
    rows: COMMITTED_ROWS_22,
    selections: &[],
};

const BOOM_ROWS_26: &[CardRow] = &[
    CardRow::Fold(EffectKind::Mult {
        key: MultKey::AsPost,
        v: 0.5,
    }),
    CardRow::Fold(EffectKind::Mult {
        key: MultKey::Hit,
        v: 2.6,
    }),
    CardRow::Fold(EffectKind::Mult {
        key: MultKey::Dot,
        v: 1.25,
    }),
    CardRow::Fold(EffectKind::Mult {
        key: MultKey::Returned,
        v: 1.25,
    }),
];

/// Here Comes the Boom at 2.6x (decision 2 of the plan): a legal override.
const BOOM_26: CardDef = CardDef {
    id: CardId::HereComesTheBoom,
    kind: CardKind::Augment,
    tier: 3,
    base: None,
    requires: None,
    unique: true,
    ready: true,
    title: "Here Comes the Boom",
    text: "Your attack speed is halved, but your hits deal 2.6x damage, Offense and SO alike. Your damage over time and returned damage deal 1.25x.",
    tags: &[MechanicTag::Dot, MechanicTag::Returned],
    rows: BOOM_ROWS_26,
    selections: &[],
};

/// Tank Engine at 1.5 HP per point blocked, cap 350 (Augment_Tuning): a trigger row's numbers, a legal override.
const TANK_ROWS_15: &[CardRow] = &[CardRow::Trigger(TriggerRow {
    on: TriggerOn::Block,
    arg: 0.0,
    once: false,
    action: TriggerAction::Fortify {
        per: 1.5,
        cap: 350.0,
    },
})];

static COMMITTED_TABLE: [CardDef; 1] = [COMMITTED_22];
static BOOM_TABLE: [CardDef; 1] = [BOOM_26];
static DOUBLED_TABLE: [CardDef; 2] = [COMMITTED_22, COMMITTED_22];

fn leak(t: Tuning) -> &'static Tuning {
    Box::leak(Box::new(t))
}

fn rework() -> SimConfig {
    SimConfig {
        augments: AugmentRule::Rework,
        ..SimConfig::default()
    }
}

fn setup(config: SimConfig, faction: FactionId) -> Sim {
    let ps = [
        Placement {
            profile: "tuning-a".into(),
            faction,
            bot: false,
            at: Vec2::new(400., 400.),
            hp: None,
            weapon0: None,
            weapon1: None,
        },
        Placement {
            profile: "tuning-b".into(),
            faction: FactionId::Holy,
            bot: false,
            at: Vec2::new(456., 400.),
            hp: None,
            weapon0: None,
            weapon1: None,
        },
    ];
    Sim::reset_active(config, arenas::practice_arena(), Secrets::from_seed(7), &ps)
}

fn slot(sim: &Sim) -> usize {
    sim.w.idx(PlayerId(1)).expect("the declared holder")
}

// ---------------------------------------------------------------------------------------------------------------------
// The override-shape guard.
// ---------------------------------------------------------------------------------------------------------------------

#[test]
fn the_shape_guard_admits_row_numbers_and_text_only() {
    let committed = CardId::Committed.definition();
    // Admitted: every catalog row against itself, and the three numbers-and-text overrides.
    for id in CardId::ALL {
        assert_eq!(
            check_override(id.definition(), id.definition()),
            Ok(()),
            "{id:?}"
        );
    }
    assert_eq!(check_override(committed, &COMMITTED_22), Ok(()));
    assert_eq!(
        check_override(CardId::HereComesTheBoom.definition(), &BOOM_26),
        Ok(())
    );
    let tank = CardDef {
        rows: TANK_ROWS_15,
        ..*CardId::TankEngine.definition()
    };
    assert_eq!(
        check_override(CardId::TankEngine.definition(), &tank),
        Ok(())
    );

    // Refused: anything else (coordinator amendment 2026-11-19: title, kind, tier, unique, flags and tags are equal).
    let mut refused: Vec<(&str, CardDef, &CardDef)> = vec![
        (
            "tier",
            CardDef {
                tier: 2,
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "kind",
            CardDef {
                kind: CardKind::Conditional,
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "unique",
            CardDef {
                unique: false,
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "ready",
            CardDef {
                ready: false,
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "base",
            CardDef {
                base: Some(BaseStatId::Ad),
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "requires",
            CardDef {
                requires: Some(BaseStatId::Ad),
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "title",
            CardDef {
                title: "Committed!",
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "tags",
            CardDef {
                tags: &[MechanicTag::OnHit],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "id",
            CardDef {
                id: CardId::MachineGun,
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a flag changed",
            CardDef {
                rows: &[
                    CardRow::Fold(EffectKind::Flag {
                        flag: EffectFlag::NoCrit,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::Damage,
                        v: 0.22,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::AttackSpeed,
                        v: 0.15,
                    }),
                ],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a stat key changed",
            CardDef {
                rows: &[
                    CardRow::Fold(EffectKind::Flag {
                        flag: EffectFlag::LockSecondSlot,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::AttackSpeed,
                        v: 0.22,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::AttackSpeed,
                        v: 0.15,
                    }),
                ],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a row added",
            CardDef {
                rows: &[
                    CardRow::Fold(EffectKind::Flag {
                        flag: EffectFlag::LockSecondSlot,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::Damage,
                        v: 0.22,
                    }),
                    CardRow::Fold(EffectKind::Stat {
                        stat: EffectStat::AttackSpeed,
                        v: 0.15,
                    }),
                    CardRow::Fold(EffectKind::Flag {
                        flag: EffectFlag::NoCrit,
                    }),
                ],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a row removed",
            CardDef {
                rows: &[CardRow::Fold(EffectKind::Stat {
                    stat: EffectStat::Damage,
                    v: 0.22,
                })],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a selection added",
            CardDef {
                selections: &[Selection {
                    faction: FactionId::Fire,
                    text: "Fire's Committed.",
                    rows: COMMITTED_ROWS_22,
                }],
                ..COMMITTED_22
            },
            committed,
        ),
        (
            "a mult key changed",
            CardDef {
                rows: &[
                    CardRow::Fold(EffectKind::Mult {
                        key: MultKey::AsPost,
                        v: 0.5,
                    }),
                    CardRow::Fold(EffectKind::Mult {
                        key: MultKey::Dot,
                        v: 2.6,
                    }),
                    CardRow::Fold(EffectKind::Mult {
                        key: MultKey::Dot,
                        v: 1.25,
                    }),
                    CardRow::Fold(EffectKind::Mult {
                        key: MultKey::Returned,
                        v: 1.25,
                    }),
                ],
                ..BOOM_26
            },
            CardId::HereComesTheBoom.definition(),
        ),
    ];
    for (on, once) in [(TriggerOn::Hit, false), (TriggerOn::Block, true)] {
        let rows: &'static [CardRow] = Box::leak(Box::new([CardRow::Trigger(TriggerRow {
            on,
            arg: 0.0,
            once,
            action: TriggerAction::Fortify {
                per: 1.5,
                cap: 350.0,
            },
        })]));
        refused.push((
            "a trigger's event or once changed",
            CardDef { rows, ..tank },
            CardId::TankEngine.definition(),
        ));
    }
    let heal: &'static [CardRow] = Box::leak(Box::new([CardRow::Trigger(TriggerRow {
        on: TriggerOn::Block,
        arg: 0.0,
        once: false,
        action: TriggerAction::Heal {
            share: 1.5,
            ms: None,
            ignore_lockout: false,
        },
    })]));
    refused.push((
        "a trigger's action changed",
        CardDef { rows: heal, ..tank },
        CardId::TankEngine.definition(),
    ));
    for (what, doctored, legacy) in &refused {
        let err = check_override(legacy, doctored);
        assert!(
            err.is_err(),
            "the guard admitted an override that changes its {what}"
        );
        assert_eq!(err.unwrap_err().id, legacy.id, "{what}: names the card");
    }
}

#[test]
fn the_test_constructor_builds_through_the_guard() {
    let numbers = legacy_numbers();
    assert!(Tuning::for_test(AugmentRule::Rework, numbers, &COMMITTED_TABLE).is_ok());
    static DOCTORED: [CardDef; 1] = [CardDef {
        tier: 2,
        ..COMMITTED_22
    }];
    static FLAGGED: [CardDef; 1] = [CardDef {
        rows: &[
            CardRow::Fold(EffectKind::Flag {
                flag: EffectFlag::Gambler,
            }),
            CardRow::Fold(EffectKind::Stat {
                stat: EffectStat::Damage,
                v: 0.22,
            }),
            CardRow::Fold(EffectKind::Stat {
                stat: EffectStat::AttackSpeed,
                v: 0.15,
            }),
        ],
        ..COMMITTED_22
    }];
    for (what, table) in [
        ("a changed tier", &DOCTORED[..]),
        ("a changed flag", &FLAGGED[..]),
        ("two rows for one card", &DOUBLED_TABLE[..]),
    ] {
        let built = Tuning::for_test(AugmentRule::Rework, numbers, table);
        assert!(built.is_err(), "the table with {what} was built");
        assert_eq!(built.unwrap_err().id, CardId::Committed, "{what}");
    }
}

/// The real override table (empty here; filled by later packets) passes the guard and holds one row per card.
#[test]
fn the_real_override_table_passes_the_guard() {
    let table = crate::cards::rework_overrides::REWORK_OVERRIDES;
    for (i, row) in table.iter().enumerate() {
        assert_eq!(
            check_override(row.id.definition(), row),
            Ok(()),
            "{:?}",
            row.id
        );
        assert!(
            table[..i].iter().all(|o| o.id != row.id),
            "{:?} has two rows",
            row.id
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Readers go through the table.
// ---------------------------------------------------------------------------------------------------------------------

/// A retuned fold row reaches the fold (`Build::rows` through `cards::grant`) and the restore check re-folds with the
/// world's own table (`Modifiers::valid` via `check_world`): the same cache is legal under the table that made it and
/// refused under the legacy table.
#[test]
fn a_retuned_fold_row_reaches_the_fold_and_the_restore_check() {
    let mut sim = setup(rework(), FactionId::Fire);
    sim.k.cards = leak(
        Tuning::for_test(AugmentRule::Rework, legacy_numbers(), &COMMITTED_TABLE)
            .expect("a numbers-and-text override is admitted"),
    );
    assert!(crate::cards::grant(
        &sim.k,
        &mut sim.w,
        PlayerId(1),
        CardId::Committed
    ));
    let i = slot(&sim);
    let p = &sim.w.players[i];
    let row = crate::modifiers::outgoing_damage(
        sim.k.cards,
        &p.core,
        &p.build,
        1.,
        OutgoingKind::Hit,
        1.,
        0,
        0,
        VsProbe::default(),
        false,
    );
    assert!(
        (row - 1.22).abs() < 1e-12,
        "the fold read the catalog's +20%, not the table's +22%: outgoing row {row}"
    );
    assert_eq!(
        crate::checkpoint::check_world(&sim.k, &sim.w),
        Ok(()),
        "the restore check refuses a fold its own world's table made"
    );
    sim.k.cards = Tuning::of(&SimConfig::default());
    assert!(
        crate::checkpoint::check_world(&sim.k, &sim.w).is_err(),
        "the restore check admitted a +22% cache under the legacy table"
    );
}

/// The G1 cap number is both the stat fold's `hit` clamp (`stat_fold/fold.rs:205-207`) and the outgoing row ceiling
/// (`modifiers/mod.rs:899-905`): a table's cap reaches both.
#[test]
fn the_outgoing_row_cap_reaches_the_fold_clamp_and_the_outgoing_ceiling() {
    // The fold clamp: Boom at 2.6 under a 2.6 cap folds 2.6 (the legacy clamp would hold it at 2.5).
    let mut sim = setup(rework(), FactionId::Fire);
    sim.k.cards = leak(
        Tuning::for_test(
            AugmentRule::Rework,
            CardNumbers {
                outgoing_row_cap: 2.6,
                ..legacy_numbers()
            },
            &BOOM_TABLE,
        )
        .expect("a numbers-and-text override is admitted"),
    );
    assert!(crate::cards::grant(
        &sim.k,
        &mut sim.w,
        PlayerId(1),
        CardId::HereComesTheBoom
    ));
    let i = slot(&sim);
    let hit = sim.w.players[i].modifiers.mult_of(MultKey::Hit);
    assert!(
        (hit - 2.6).abs() < 1e-12,
        "the fold's hit mult is {hit}, not the table's 2.6 under the table's 2.6 cap"
    );
    assert_eq!(crate::checkpoint::check_world(&sim.k, &sim.w), Ok(()));

    // The outgoing ceiling: Glass Cannon's +35% under a test cap of 1.2 stops at 1.2 (legacy 2.5 leaves it at 1.35).
    let mut sim = setup(rework(), FactionId::Fire);
    sim.k.cards = leak(
        Tuning::for_test(
            AugmentRule::Rework,
            CardNumbers {
                outgoing_row_cap: 1.2,
                ..legacy_numbers()
            },
            &[],
        )
        .expect("an empty table"),
    );
    assert!(crate::cards::grant(
        &sim.k,
        &mut sim.w,
        PlayerId(1),
        CardId::GlassCannon
    ));
    let i = slot(&sim);
    let p = &sim.w.players[i];
    let row = crate::modifiers::outgoing_damage(
        sim.k.cards,
        &p.core,
        &p.build,
        1.,
        OutgoingKind::Hit,
        1.,
        0,
        0,
        VsProbe::default(),
        false,
    );
    assert!(
        (row - 1.2).abs() < 1e-12,
        "the outgoing row is {row}, not capped at the table's 1.2"
    );
}

/// Gambler reads its two chances and two factors from the table (the plan's decision 4 splits the chances).
#[test]
fn gambler_reads_its_chances_and_factors_from_the_table() {
    let mut sim = setup(rework(), FactionId::Fire);
    let cases = [
        (
            CardNumbers {
                gambler_up_chance: 1.,
                gambler_down_chance: 0.,
                gambler_up: 3.,
                ..legacy_numbers()
            },
            3.,
        ),
        (
            CardNumbers {
                gambler_up_chance: 0.,
                gambler_down_chance: 1.,
                gambler_down: 0.25,
                ..legacy_numbers()
            },
            0.25,
        ),
        (
            CardNumbers {
                gambler_up_chance: 0.,
                gambler_down_chance: 0.,
                ..legacy_numbers()
            },
            1.,
        ),
    ];
    for (numbers, want) in cases {
        sim.k.cards = leak(Tuning::for_test(AugmentRule::Rework, numbers, &[]).unwrap());
        for index in 0..32 {
            let got = crate::cards::gambler_factor(&sim.k, &sim.w, PlayerId(1), PlayerId(2), index);
            assert_eq!(got, want, "entry {index} under {numbers:?}");
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// The ranged-packet restore envelope.
// ---------------------------------------------------------------------------------------------------------------------

/// Under `Rework` the envelope (`maximum_packet`, read by `valid_ranged_packet` for projectiles and pending actions)
/// covers every fold card a live player can hold, not only `cards::SUPPORTED`: a fighter at the most it can farm and
/// boost, holding a fold card (or all of them), fires a bow or wand packet the restore check admits. The envelope stays
/// finite and within a few times the legacy one (a forged packet is still refused).
#[test]
fn the_rework_ranged_envelope_covers_the_fold_cards() {
    use crate::WeaponId;
    let fold_cards = [
        CardId::ArcaneOverload,
        CardId::Committed,
        CardId::MachineGun,
        CardId::HereComesTheBoom,
        CardId::FistsOfFury,
        CardId::RaceToZero,
        CardId::GlassCannon,
        CardId::VampiricPact,
    ];
    let mut builds: Vec<Vec<CardId>> = fold_cards.iter().map(|c| vec![*c]).collect();
    builds.push(fold_cards.to_vec());
    let legacy = Tuning::of(&SimConfig::default());
    let max_picks = crate::stat_fold::default_definition().chest_boost_max_picks as u8;
    for faction in FactionId::ALL {
        for cards in &builds {
            let mut sim = setup(rework(), faction);
            let i = slot(&sim);
            sim.w.players[i].build.boosts = [max_picks; 8];
            sim.w.players[i].farm = crate::modifiers::Farm::maximum();
            for card in cards {
                assert!(crate::cards::grant(&sim.k, &mut sim.w, PlayerId(1), *card));
            }
            let p = &sim.w.players[i];
            for weapon in [WeaponId::Bow, WeaponId::Wand] {
                let packet = crate::modifiers::packet_with_sheet(
                    &p.core,
                    weapon,
                    &crate::content::paced(faction),
                    crate::content::faction(faction).hp,
                );
                let envelope = crate::modifiers::maximum_packet(sim.k.cards, weapon);
                assert!(
                    crate::checkpoint::valid_ranged_packet(sim.k.cards, packet, weapon),
                    "{faction:?} {weapon:?} {cards:?}: packet {} above the Rework envelope {envelope}",
                    packet.raw
                );
                let floor = crate::modifiers::maximum_packet(legacy, weapon);
                assert!(
                    envelope.is_finite() && envelope <= 4. * floor,
                    "{weapon:?}: the Rework envelope {envelope} is not a bound (legacy {floor})"
                );
            }
        }
    }
}
