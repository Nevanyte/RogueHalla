using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    public sealed partial class SpriteActorPresenter
    {
        static void SelectFireWeaponBody(Art actor, ActorKind kind, FactionId? faction, WeaponId? weapon,
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

                bool alternate = phase == StormInkWeaponMotion.Phase.Ready
                    ? (actor.FireSwordReleaseCount & 1) != 0
                    : actor.FireSwordReleaseCount > 0 && (actor.FireSwordReleaseCount & 1) == 0;
                int swordPoseColumn = StormInkActors.FireSwordPoseColumn(phase, progress, alternate);
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
    }
}
