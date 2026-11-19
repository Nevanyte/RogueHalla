//! Reliable field rows pair legacy spell geometry with anonymous Top 25 cues.
//! Every row comes from the simulation's already filtered observation.
use rh_protocol::{WireField, WireProjectile};
use rh_sim::Observation;

pub(super) fn fields(observation: &Observation) -> Vec<WireField> {
    let mut rows: Vec<_> = observation
        .fields
        .iter()
        .map(|f| WireField {
            kind: f.kind as u8,
            ability: f.ability as u8,
            owner: f.owner.map(|id| id.0),
            subject: f.subject.map(|id| id.0),
            x: f.x as f32,
            y: f.y as f32,
            x2: f.x2 as f32,
            y2: f.y2 as f32,
            r: f.r as f32,
            vx: f.vx as f32,
            vy: f.vy as f32,
            started_at: f.started_at,
            ends_at: f.ends_at,
            // P-FROST-WALL-2: a wall's tiles (the sim's barrier row); empty, so no allocation, for every other kind.
            tiles: f.tiles.clone(),
        })
        .collect();
    use rh_protocol::state::{
        FIELD_KIND_QUEST_BOLAS, FIELD_KIND_QUEST_FALCON, FIELD_KIND_QUEST_HOOK,
        FIELD_KIND_QUEST_SEED, FIELD_KIND_QUEST_TORNADO, FIELD_KIND_QUEST_TURRET,
        FIELD_KIND_QUEST_WARD,
    };
    use rh_sim::{DeploymentKind, SpellCueKind};
    // These render-only rows carry delivered geometry. No native deployment,
    // owner or target identity becomes a capability or a wire reference.
    let cue = |kind, at: rh_sim::Vec2, end: rh_sim::Vec2, ends_at| WireField {
        kind,
        ability: 0,
        owner: None,
        subject: None,
        x: at.x as f32,
        y: at.y as f32,
        x2: end.x as f32,
        y2: end.y as f32,
        r: 0.,
        vx: 0.,
        vy: 0.,
        started_at: 0.,
        ends_at,
        tiles: Vec::new(),
    };
    rows.extend(observation.quest_deployments.iter().map(|deployment| {
        let kind = match deployment.kind {
            DeploymentKind::Turret => FIELD_KIND_QUEST_TURRET,
            DeploymentKind::LanternWard => FIELD_KIND_QUEST_WARD,
            DeploymentKind::ScoutFalcon => FIELD_KIND_QUEST_FALCON,
        };
        cue(
            kind,
            deployment.at,
            deployment.end.unwrap_or(deployment.at),
            deployment.until,
        )
    }));
    rows.extend(observation.quest_spell_cues.iter().map(|spell| {
        let kind = match spell.kind {
            SpellCueKind::Hook => FIELD_KIND_QUEST_HOOK,
            SpellCueKind::Bolas => FIELD_KIND_QUEST_BOLAS,
            SpellCueKind::Tornado => FIELD_KIND_QUEST_TORNADO,
            SpellCueKind::ForgetMeNotSeed => FIELD_KIND_QUEST_SEED,
        };
        cue(kind, spell.at, spell.at, spell.lands_at.unwrap_or(0.))
    }));
    use rh_protocol::state::{
        FIELD_KIND_QUEST_BOUNTY, FIELD_KIND_QUEST_CROWN, FIELD_KIND_QUEST_FORT,
        FIELD_KIND_QUEST_MEDITATION, FIELD_KIND_QUEST_SMUGGLER,
    };
    use rh_sim::observe::QuestPublicMarkKind;
    rows.extend(observation.quest_public_marks.iter().map(|mark| {
        let kind = match mark.kind {
            QuestPublicMarkKind::Crown => FIELD_KIND_QUEST_CROWN,
            QuestPublicMarkKind::Smuggler => FIELD_KIND_QUEST_SMUGGLER,
            QuestPublicMarkKind::Fort => FIELD_KIND_QUEST_FORT,
            QuestPublicMarkKind::Meditation => FIELD_KIND_QUEST_MEDITATION,
            QuestPublicMarkKind::Bounty => FIELD_KIND_QUEST_BOUNTY,
        };
        cue(kind, mark.at, mark.at, 0.)
    }));
    rows
}

pub(super) fn projectiles(observation: &Observation) -> Vec<WireProjectile> {
    observation
        .projectiles
        .iter()
        .map(|p| WireProjectile {
            height: p.height,
            kind: match p.kind {
                rh_sim::ProjectileKind::Arrow => 0,
                rh_sim::ProjectileKind::Ball => 1,
            },
            x: p.x,
            y: p.y,
            facing: p.facing,
            owner: p.owner.map(|id| id.0),
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::super::CollectionPublisher;
    use super::*;
    use rh_protocol::state::{
        FIELD_KIND_QUEST_SMUGGLER, FIELD_KIND_QUEST_TORNADO, FIELD_KIND_QUEST_TURRET,
    };
    use rh_protocol::{ServerMsg, StateItem};
    use rh_sim::observe::{QuestPublicMark, QuestPublicMarkKind};
    use rh_sim::{DeploymentKind, DeploymentView, PlayerId, SpellCue, SpellCueKind, Vec2};

    #[test]
    fn visible_cues_are_anonymous_reliable_replacements_and_clear_when_no_longer_observed() {
        let at = Vec2::new(88., 96.);
        let mut obs = Observation {
            tick: 5,
            quest_deployments: vec![DeploymentView {
                id: 123,
                owner: Some(PlayerId(128)),
                kind: DeploymentKind::Turret,
                at,
                end: None,
                hp: 500.,
                until: 2000.,
            }],
            quest_spell_cues: vec![SpellCue {
                kind: SpellCueKind::Tornado,
                at,
                lands_at: Some(900.),
            }],
            quest_public_marks: vec![QuestPublicMark {
                kind: QuestPublicMarkKind::Smuggler,
                at,
            }],
            ..Default::default()
        };
        let mut publisher = CollectionPublisher::default();
        let pending = publisher.prepare(&obs, false, false, false).unwrap();
        let ServerMsg::State { items, .. } =
            ServerMsg::decode(pending.frame.as_ref().unwrap()).unwrap()
        else {
            panic!("state must round-trip");
        };
        let rows = items
            .iter()
            .find_map(|item| match item {
                StateItem::Fields(rows) => Some(rows),
                _ => None,
            })
            .unwrap();
        assert_eq!(
            rows.iter().map(|row| row.kind).collect::<Vec<_>>(),
            [
                FIELD_KIND_QUEST_TURRET,
                FIELD_KIND_QUEST_TORNADO,
                FIELD_KIND_QUEST_SMUGGLER
            ],
        );
        for row in rows {
            assert_eq!((row.x, row.y), (88., 96.));
            assert_eq!((row.owner, row.subject), (None, None));
            assert!(row.tiles.is_empty());
        }
        assert_eq!(rows[1].ends_at, 900.);
        pending.commit(&mut publisher);
        obs.tick += 1;
        obs.quest_deployments.clear();
        obs.quest_spell_cues.clear();
        obs.quest_public_marks.clear();
        let pending = publisher.prepare(&obs, false, false, false).unwrap();
        assert!(matches!(
            ServerMsg::decode(pending.frame.as_ref().unwrap()),
            Ok(ServerMsg::State { items, .. }) if items == [StateItem::Fields(Vec::new())]
        ));
    }
}
