using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Routes the completed Fire east-facing bow performance through normal player body rendering.</summary>
    public sealed partial class SpriteActorPresenter
    {
        static StormInkFireBowAuthoring FireBowBodySource(Sprite body)
        {
            if (body == null) return null;
            var art = StormInkFireBowAuthoring.Load();
            return art != null && art.TryHand(body, false, out _) ? art : null;
        }

        static Sprite SelectFireBowBody(Art actor, ActorKind kind, FactionId? faction, WeaponId? weapon,
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
            if (weaponAction && deliveredDraw)
            {
                bool alternate = phase == StormInkWeaponMotion.Phase.Ready
                    ? (actor.FireBowShotCount & 1) != 0
                    : actor.FireBowShotCount > 0 && (actor.FireBowShotCount & 1) == 0;
                return art.Frame(row, phase, progress, alternate);
            }
            if (sharedRig == null) return null;
            return sharedRig.Frame(row, stride ? StormInkFireAxePose.Stride : StormInkFireAxePose.Guard, stage);
        }
    }
}
