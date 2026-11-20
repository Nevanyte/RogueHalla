//! The Wish cooldown levers the restore envelope (`abilities/checkpoint.rs`, `wish_cooldowns`) is built on; moved out of
//! `checkpoint.rs` by P-AUG-NATIVE-3b's stub so the envelope's file stays the implementer's.
use crate::cards::{CardId, CardRow, Tuning};
use crate::stat_fold::{EffectKind, MultKey};

/// `wish_cooldowns` (the restore's envelope of a spend's cooldown) holds while exactly one card, unique, carries a
/// `WishCooldown` row and that row only lengthens, in both tables: a build that only grows then had either no
/// multiplier or today's at every cast. A second lever, or one that shortens, needs a new envelope here.
#[test]
fn one_unique_card_only_lengthens_the_wish_cooldown_in_both_tables() {
    for augments in [crate::AugmentRule::Legacy, crate::AugmentRule::Rework] {
        let t = Tuning::of(&crate::SimConfig {
            augments,
            ..crate::SimConfig::default()
        });
        let mut levers = Vec::new();
        for id in CardId::ALL {
            let def = t.def(id);
            for row in def
                .rows
                .iter()
                .chain(def.selections.iter().flat_map(|s| s.rows.iter()))
            {
                if let CardRow::Fold(EffectKind::Mult {
                    key: MultKey::WishCooldown,
                    v,
                }) = row
                {
                    assert!(
                        *v >= 1.,
                        "{augments:?} {id:?}: a Wish cooldown factor below 1"
                    );
                    levers.push(id);
                }
            }
        }
        assert_eq!(levers, [CardId::ArcaneOverload], "{augments:?}");
        assert!(t.def(CardId::ArcaneOverload).unique);
    }
}
