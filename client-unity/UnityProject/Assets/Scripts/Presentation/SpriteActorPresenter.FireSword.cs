using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Selects the Fire champion’s sword and axe body drawings and the matching weapon presentation.</summary>
    public sealed partial class SpriteActorPresenter
    {
        void SelectFireWeaponBody(Art actor, uint actorId, ActorKind kind, FactionId? faction, WeaponId? weapon,
            int direction, StormInkWeaponMotion.Phase phase, float progress, bool useWeaponPhase,
            bool stride, int stage, bool reduced, StormInkFireSwordAuthoring swordArt,
            out Sprite axeBody, out Sprite swordBody, out float swordAngleOffset, out int swordDepthOffset)
        {
            axeBody = null;
            swordBody = null;
            swordAngleOffset = 0f;
            swordDepthOffset = 0;
            if (kind != ActorKind.Player || faction != FactionId.Fire ||
                (weapon != WeaponId.Axe && weapon != WeaponId.Sword)) return;

            if (weapon == WeaponId.Sword)
            {
                if (!useWeaponPhase)
                {
                    axeBody = StormInkActors.FireAxeFrame(kind, faction, weapon, direction,
                        StormInkWeaponMotion.Phase.Guard, stride, stage);
                    return;
                }

                int studioColumn = StudioFireSwordPoseColumn(actorId);
                bool alternate = studioColumn >= 0 ? studioColumn >= 5 : phase == StormInkWeaponMotion.Phase.Ready
                    ? (actor.FireSwordReleaseCount & 1) != 0
                    : actor.FireSwordReleaseCount > 0 && (actor.FireSwordReleaseCount & 1) == 0;
                int swordPoseColumn = studioColumn >= 0 ? studioColumn :
                    StormInkActors.FireSwordPoseColumn(phase, progress, alternate);
                swordBody = StormInkActors.FireSwordFrame(kind, faction, weapon, direction,
                    phase, progress, alternate);
                var artPhase = StormInkActors.SwordMotionPhase(phase);
                if (swordArt != null && artPhase.HasValue)
                {
                    int row = StormInkActors.PoseRow(direction);
                    swordAngleOffset = swordArt.BladeAngleOffset(row, artPhase.Value, progress, alternate, reduced);
                    swordDepthOffset = swordArt.WeaponDepthOffset(row, swordPoseColumn);
                }
                return;
            }

            bool alternateAxe = phase == StormInkWeaponMotion.Phase.Ready
                ? (actor.FireAxeReleaseCount & 1) != 0
                : actor.FireAxeReleaseCount > 0 && (actor.FireAxeReleaseCount & 1) == 0;
            axeBody = StormInkActors.FireAxeFrame(kind, faction, weapon, direction,
                useWeaponPhase ? phase : StormInkWeaponMotion.Phase.Guard, stride, stage, alternateAxe);
        }

#if UNITY_EDITOR
        uint _studioSwordPreviewActor;
        int _studioSwordPreviewColumn = -1;

        public void SetStudioFireSwordPose(uint actorId, int column)
        {
            _studioSwordPreviewActor = actorId;
            _studioSwordPreviewColumn = column;
        }
#endif

        StormInkFireSwordAuthoring PrepareFireSwordPresentation(uint actorId, ActorKind kind, FactionId? faction, WeaponId? weapon,
            float facing, ref StormInkWeaponMotion.Phase phase,
            ref float progress, ref float poseFacing, ref bool useWeaponPhase)
        {
            var swordArt = kind == ActorKind.Player && faction == FactionId.Fire && weapon == WeaponId.Sword
                ? StormInkFireSwordAuthoring.Load() : null;
#if UNITY_EDITOR
            int column = StudioFireSwordPoseColumn(actorId);
            if (column >= 0 && kind == ActorKind.Player && faction == FactionId.Fire &&
                weapon == WeaponId.Sword && swordArt != null)
            {
                swordArt.PreviewParameters(column, out var previewPhase, out progress);
                phase = previewPhase switch
                {
                    StormInkFireSwordMotionPhase.Windup => StormInkWeaponMotion.Phase.Ready,
                    StormInkFireSwordMotionPhase.Strike => StormInkWeaponMotion.Phase.Strike,
                    _ => StormInkWeaponMotion.Phase.Follow
                };
                poseFacing = facing;
                useWeaponPhase = true;
            }
#endif
            return swordArt;
        }

        int StudioFireSwordPoseColumn(uint actorId)
        {
#if UNITY_EDITOR
            return actorId == _studioSwordPreviewActor ? _studioSwordPreviewColumn : -1;
#else
            return -1;
#endif
        }
    }
}
