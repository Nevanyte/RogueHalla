//! Experimental camp choice, isolated from ordinary Farm execution.
use super::*;

impl Farm {
    pub(super) fn choose_lab(
        &mut self,
        cx: &mut Cx<'_>,
        order: &mut Vec<(f64, &CampSeen)>,
        at: Vec2,
        me: FactionId,
        now: f64,
    ) {
        if let Some(route) = self.lab_route {
            if !order.is_empty() {
                // All arms see the same eligible camps and use the same path ETA and clear-time estimate.
                let horizon = cx.tier_params().route.horizon as usize;
                let choices: Vec<(f64, &CampSeen)> = order
                    .iter()
                    .enumerate()
                    .map(|(n, (_, c))| {
                        let walk = if n < horizon {
                            eta_minutes(cx.paths, at, c.at)
                                .unwrap_or_else(|| straight_minutes(at, c.at))
                        } else {
                            straight_minutes(at, c.at)
                        };
                        (walk, *c)
                    })
                    .collect();
                if matches!(
                    route,
                    LabRoute::FocusRoute | LabRoute::FocusRiskControl | LabRoute::FocusStatRisk(_)
                ) {
                    let kept = focus_shortlist(&choices, me, self.focus_origin);
                    order.retain(|(_, c)| kept.contains(&c.index));
                    return;
                }
                let selected = choose_index(
                    route,
                    &choices,
                    me,
                    now,
                    cx.prices.kill_seconds,
                    &mut self.random_pick,
                );
                if matches!(route, LabRoute::SafeTargetBuild(_)) {
                    let kept = safe_shortlist(&choices, selected);
                    order.retain(|(_, c)| kept.contains(&c.index));
                    return;
                }
                let chosen = order[selected];
                order.clear();
                order.push(chosen);
            }
        }
    }
}

/// HUD reward codes are the actual camp stat, including an off-theme roll.
pub(super) fn focus_code(faction: FactionId) -> Option<u8> {
    Some(match faction {
        FactionId::Fire => 2,                                           // Damage
        FactionId::Plague => 1,                                         // Soul Offense / AP
        FactionId::Sentenced | FactionId::Frost | FactionId::Holy => 3, // Attack speed
    })
}

pub(super) fn farm_stat(code: u8) -> Option<crate::bots::build_route::FarmStat> {
    use crate::bots::build_route::FarmStat;
    match code {
        0 => Some(FarmStat::Ad),
        1 => Some(FarmStat::Ap),
        2 => Some(FarmStat::Damage),
        3 => Some(FarmStat::AttackSpeed),
        4 => Some(FarmStat::Lifesteal),
        5 => Some(FarmStat::MaxHp),
        6 => Some(FarmStat::Armor),
        7 => Some(FarmStat::Shield),
        8 => Some(FarmStat::MoveSpeed),
        _ => None,
    }
}

/// Two local fallback camps remain for the ordinary risk arbiter. A focus destination
/// must be a cheap local opening (4 s) or a modest follow-on detour (10 s).
fn focus_shortlist(choices: &[(f64, &CampSeen)], me: FactionId, origin: Option<u32>) -> Vec<u32> {
    let mut near: Vec<usize> = (0..choices.len()).collect();
    near.sort_by(|&a, &b| {
        choices[a]
            .0
            .total_cmp(&choices[b].0)
            .then(choices[a].1.index.cmp(&choices[b].1.index))
    });
    let closest = choices[near[0]].0;
    let detour = if origin.is_some() {
        10. / 60.
    } else {
        4. / 60.
    };
    let mut kept: Vec<u32> = near.iter().take(2).map(|&i| choices[i].1.index).collect();
    for (walk, c) in choices {
        if c.reward_stat == focus_code(me)
            && origin != Some(c.index)
            && *walk <= closest + detour
            && !kept.contains(&c.index)
        {
            kept.push(c.index);
        }
    }
    kept
}

/// The target plus two shortest ETA fallbacks; the arbiter prices their real death risk.
fn safe_shortlist(choices: &[(f64, &CampSeen)], selected: usize) -> Vec<u32> {
    let mut near: Vec<usize> = (0..choices.len()).collect();
    near.sort_by(|&a, &b| {
        choices[a]
            .0
            .total_cmp(&choices[b].0)
            .then(choices[a].1.index.cmp(&choices[b].1.index))
    });
    near.iter()
        .take(2)
        .map(|&i| choices[i].1.index)
        .chain(std::iter::once(choices[selected].1.index))
        .collect()
}

/// Selects only from the already eligible, already timed candidate set.
fn choose_index(
    route: LabRoute,
    choices: &[(f64, &CampSeen)],
    me: FactionId,
    now: f64,
    kill_seconds: f64,
    random_pick: &mut Option<u32>,
) -> usize {
    assert!(!choices.is_empty());
    match route {
        LabRoute::Nearest => choices
            .iter()
            .enumerate()
            .min_by(|a, b| {
                a.1 .0
                    .total_cmp(&b.1 .0)
                    .then(a.1 .1.index.cmp(&b.1 .1.index))
            })
            .map_or(0, |(i, _)| i),
        LabRoute::FocusRoute | LabRoute::FocusRiskControl | LabRoute::FocusStatRisk(_) => {
            unreachable!("focus route uses the risk-arbiter shortlist")
        }
        LabRoute::TargetBuild(build)
        | LabRoute::LocalTarget(build)
        | LabRoute::CalibratedBuild(build)
        | LabRoute::SafeTargetBuild(build) => {
            use crate::bots::build_route::{FarmStat, BUILD_GOALS, STAT_FLOOR};
            let goal = &BUILD_GOALS[me as usize][build.min(1)];
            let stat_of = |code| match code {
                0 => Some(FarmStat::Ad),
                1 => Some(FarmStat::Ap),
                2 => Some(FarmStat::Damage),
                3 => Some(FarmStat::AttackSpeed),
                4 => Some(FarmStat::Lifesteal),
                5 => Some(FarmStat::MaxHp),
                6 => Some(FarmStat::Armor),
                7 => Some(FarmStat::Shield),
                8 => Some(FarmStat::MoveSpeed),
                _ => None,
            };
            let closest = choices
                .iter()
                .map(|(walk, _)| *walk)
                .min_by(f64::total_cmp)
                .unwrap_or(0.);
            choices
                .iter()
                .enumerate()
                .filter(|(_, (walk, _))| {
                    !matches!(route, LabRoute::LocalTarget(_)) || *walk <= closest + 4. / 60.
                })
                .max_by(|a, b| {
                    let score = |(walk, c): &(f64, &CampSeen)| {
                        let weight = c
                            .reward_stat
                            .and_then(stat_of)
                            .map_or(STAT_FLOOR, |s| goal.weight(s).max(STAT_FLOOR));
                        // The calibrated arm uses the source planner's 4 s per mob. Ordinary and
                        // historical lab routes keep RaidBot's existing 10 s estimate.
                        let clear_seconds = if matches!(
                            route,
                            LabRoute::CalibratedBuild(_) | LabRoute::SafeTargetBuild(_)
                        ) {
                            4.
                        } else {
                            kill_seconds
                        };
                        let clear = f64::from(c.alive) * clear_seconds / 60.;
                        weight * f64::from(c.alive) / (walk + clear).max(0.001)
                    };
                    score(a.1)
                        .total_cmp(&score(b.1))
                        .then_with(|| b.1 .1.index.cmp(&a.1 .1.index))
                })
                .map_or(0, |(i, _)| i)
        }
        LabRoute::RandomNearby(seed) => {
            if let Some(index) =
                (*random_pick).and_then(|held| choices.iter().position(|(_, c)| c.index == held))
            {
                index
            } else {
                // A separate deterministic stream: no draw from the policy's decision RNG.
                let x = seed ^ (now as u64 / 2_000).wrapping_mul(0x9e3779b97f4a7c15);
                let x = (x ^ (x >> 30)).wrapping_mul(0xbf58476d1ce4e5b9);
                let x = (x ^ (x >> 27)).wrapping_mul(0x94d049bb133111eb);
                let mut nearest: Vec<usize> = (0..choices.len()).collect();
                nearest.sort_by(|&a, &b| {
                    choices[a]
                        .0
                        .total_cmp(&choices[b].0)
                        .then(choices[a].1.index.cmp(&choices[b].1.index))
                });
                let selected = nearest[((x ^ (x >> 31)) % nearest.len().min(5) as u64) as usize];
                *random_pick = Some(choices[selected].1.index);
                selected
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rh_sim::mapdata::Zone;

    fn camp(index: u32, zone: Zone, stat: u8) -> CampSeen {
        CampSeen {
            index,
            at: Vec2::new(0., 0.),
            zone,
            elite_spot: -1,
            mob_type: 0,
            reward_stat: Some(stat),
            elite: None,
            alive: 4,
            state: 0,
            cleared_at: 0.,
            respawn_at: 0.,
            ward: None,
        }
    }

    #[test]
    fn focus_shortlist_uses_visible_stat_and_bounds_opening_detour() {
        let camps = [
            camp(0, Zone::Fire, 0),
            camp(1, Zone::Fire, 2),
            camp(2, Zone::Fire, 2),
            camp(3, Zone::Fire, 2),
        ];
        let choices = [
            (0., &camps[0]),
            (3. / 60., &camps[1]),
            (9. / 60., &camps[2]),
            (18. / 60., &camps[3]),
        ];
        assert_eq!(focus_shortlist(&choices, FactionId::Fire, None), vec![0, 1]);
        assert_eq!(
            focus_shortlist(&choices, FactionId::Fire, Some(1)),
            vec![0, 1, 2]
        );
        assert_eq!(
            focus_shortlist(&choices, FactionId::Plague, None),
            vec![0, 1]
        );
    }

    #[test]
    fn safe_shortlist_keeps_distant_target_and_two_eta_fallbacks() {
        let camps: Vec<_> = (0..4).map(|i| camp(i, Zone::Fire, 2)).collect();
        let choices = [
            (0.3, &camps[0]),
            (0.1, &camps[1]),
            (0.2, &camps[2]),
            (0.4, &camps[3]),
        ];
        assert_eq!(safe_shortlist(&choices, 3), vec![1, 2, 3]);
    }

    #[test]
    fn off_theme_hud_reward_beats_zone_label_when_detour_pays() {
        let near = camp(1, Zone::Fire, 8); // Fire zone, move speed.
        let far = camp(2, Zone::Frost, 2); // Frost zone, visible damage reward.
        let choices = [(0.1, &near), (0.2, &far)];
        assert_eq!(
            choose_index(
                LabRoute::Nearest,
                &choices,
                FactionId::Fire,
                0.,
                4.,
                &mut None
            ),
            0
        );
        assert_eq!(
            choose_index(
                LabRoute::TargetBuild(0),
                &choices,
                FactionId::Fire,
                0.,
                4.,
                &mut None
            ),
            1
        );
        assert_eq!(
            choose_index(
                LabRoute::LocalTarget(0),
                &choices,
                FactionId::Fire,
                0.,
                4.,
                &mut None
            ),
            0
        );
        let within_four_seconds = [(0.1, &near), (0.15, &far)];
        assert_eq!(
            choose_index(
                LabRoute::LocalTarget(0),
                &within_four_seconds,
                FactionId::Fire,
                0.,
                4.,
                &mut None
            ),
            1
        );
        let longer = [(0.1, &near), (2.0, &far)];
        let excessive_detour = [(0.1, &near), (1.5, &far)];
        assert_eq!(
            choose_index(
                LabRoute::TargetBuild(0),
                &excessive_detour,
                FactionId::Fire,
                0.,
                10.,
                &mut None
            ),
            1
        );
        assert_eq!(
            choose_index(
                LabRoute::CalibratedBuild(0),
                &excessive_detour,
                FactionId::Fire,
                0.,
                10.,
                &mut None
            ),
            0
        );
        assert_eq!(
            choose_index(
                LabRoute::TargetBuild(0),
                &longer,
                FactionId::Fire,
                0.,
                4.,
                &mut None
            ),
            0
        );
    }

    #[test]
    fn random_nearby_is_deterministic_and_sticky_until_unavailable() {
        let camps: Vec<_> = (0..6).map(|i| camp(i, Zone::Fire, 2)).collect();
        let choices: Vec<_> = camps
            .iter()
            .enumerate()
            .map(|(i, c)| (i as f64, c))
            .collect();
        let mut first = None;
        let picked = choose_index(
            LabRoute::RandomNearby(17),
            &choices,
            FactionId::Fire,
            0.,
            4.,
            &mut first,
        );
        assert_eq!(first, Some(choices[picked].1.index));
        assert!(picked < 5);
        assert_eq!(
            choose_index(
                LabRoute::RandomNearby(17),
                &choices,
                FactionId::Fire,
                99_999.,
                4.,
                &mut first
            ),
            picked
        );
        let remaining: Vec<_> = choices
            .iter()
            .copied()
            .filter(|(_, c)| Some(c.index) != first)
            .collect();
        let next = choose_index(
            LabRoute::RandomNearby(17),
            &remaining,
            FactionId::Fire,
            99_999.,
            4.,
            &mut first,
        );
        assert_ne!(first, Some(choices[picked].1.index));
        assert_eq!(first, Some(remaining[next].1.index));
        let mut replay = None;
        assert_eq!(
            choose_index(
                LabRoute::RandomNearby(17),
                &choices,
                FactionId::Fire,
                0.,
                4.,
                &mut replay
            ),
            picked
        );
    }
}
