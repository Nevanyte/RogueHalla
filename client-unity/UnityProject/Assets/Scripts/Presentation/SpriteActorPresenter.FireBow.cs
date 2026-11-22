using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Routes restored Fire bow performances through normal player body rendering.</summary>
    public sealed partial class SpriteActorPresenter
    {
        static StormInkFireBowAuthoring FireBowBodySource(Sprite body)
        {
            if (body == null) return null;
            var art = StormInkFireBowAuthoring.Load();
            return art != null && art.TryHand(body, false, out _) ? art : null;
        }

        Sprite SelectFireBowBody(Art actor, uint actorId, ActorKind kind, FactionId? faction, WeaponId? weapon,
            int direction, StormInkWeaponMotion.Phase phase, float progress, bool weaponAction, bool stride,
            int stage, StormInkFireAxeAuthoring sharedRig)
        {
            bool deliveredDraw = actor.DrawStartedAt != 0 || phase == StormInkWeaponMotion.Phase.Strike ||
                phase == StormInkWeaponMotion.Phase.Follow;
            if (kind != ActorKind.Player || faction != FactionId.Fire || weapon != WeaponId.Bow)
                return null;
            var art = StormInkFireBowAuthoring.Load();
            if (art == null) return null;
            int row = StormInkActors.PoseRow(direction);
            if (!art.IsReady(row)) return null;
            bool studioPreview = false;
            bool alternate = false;
#if UNITY_EDITOR
            studioPreview = TryStudioFirePose(actorId, WeaponId.Bow, out _, out _, out alternate);
#endif
            if (studioPreview || (weaponAction && deliveredDraw))
            {
#if UNITY_EDITOR
                if (!studioPreview)
#endif
                    alternate = phase == StormInkWeaponMotion.Phase.Ready
                        ? (actor.FireBowShotCount & 1) != 0
                        : actor.FireBowShotCount > 0 && (actor.FireBowShotCount & 1) == 0;
                return art.Frame(row, phase, progress, alternate);
            }
            if (sharedRig == null) return null;
            return sharedRig.Frame(row, stride ? StormInkFireAxePose.Stride : StormInkFireAxePose.Guard, stage);
        }
    }
}
