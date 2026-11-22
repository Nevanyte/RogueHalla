using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Maintains an already visible weapon pose during a continuous, unambiguous NPC art handoff.</summary>
    public sealed partial class SpriteActorPresenter
    {
        // Half a map tile in a single observation. The normal fade matcher has a much wider range.
        const float PoseContinuityRadius = 16f;

        void MarkImmediateMotionSuccessors(List<InterpActor> observed)
        {
            // A ghost that missed its immediately following observation keeps only its frozen picture.
            foreach (var ghost in _ghosts)
                if (ghost.MotionSuccessor != 0) ClearWeaponMotion(ghost);
            if (observed == null || !ContinuityEnabled || !ExitFadesAllowed || _remove.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (uint outgoing in _remove)
            {
                Art art = _active[outgoing];
                if (art.Kind != ActorKind.RegionalMob || art.Dead ||
                    (art.WindupUntil <= now && art.AttackUntil <= now)) continue;
                uint next = 0;
                foreach (var row in observed)
                {
                    if (_active.ContainsKey(row.Id) || !SameVisibleBody(art, row)) continue;
                    if (next != 0) { next = 0; break; }
                    next = row.Id;
                    foreach (uint other in _remove)
                        if (other != outgoing && SameVisibleBody(_active[other], row)) { next = 0; break; }
                    if (next == 0) break;
                }
                art.MotionSuccessor = next;
            }
        }

        static bool SameVisibleBody(Art art, in InterpActor row)
        {
            if (row.Kind != ActorKind.RegionalMob || art.Kind != row.Kind || art.Dead || row.Status != ActorStatus.Alive ||
                !row.MobType.HasValue || row.MobType != art.MobType || row.Faction != art.Faction ||
                !row.Weapon.HasValue || row.Weapon != art.WeaponId || Indicators.HiddenAlpha(row.Effects, false) <= 0f)
                return false;
            Vector3 drawn = art.Root.transform.position;
            float dx = row.X - drawn.x / ArenaView.Scale;
            float dy = row.Y + drawn.y / (ArenaView.Scale * ArenaView.GroundSquash);
            return dx * dx + dy * dy <= PoseContinuityRadius * PoseContinuityRadius;
        }
    }
}
