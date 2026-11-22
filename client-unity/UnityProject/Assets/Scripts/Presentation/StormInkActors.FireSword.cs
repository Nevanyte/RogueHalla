using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Fire's two painted, eight-direction sword performances.</summary>
    public static partial class StormInkActors
    {
        internal static Sprite FireSwordFrame(ActorKind kind, FactionId? faction, WeaponId? weapon,
            int direction, StormInkWeaponMotion.Phase phase, float progress, bool alternate)
        {
            if (kind != ActorKind.Player || faction != FactionId.Fire || weapon != WeaponId.Sword) return null;
            var artPhase = SwordMotionPhase(phase);
            return artPhase.HasValue
                ? StormInkFireSwordAuthoring.Load()?.Frame(PoseRow(direction), artPhase.Value, progress, alternate)
                : null;
        }

        internal static int FireSwordPoseColumn(StormInkWeaponMotion.Phase phase, float progress, bool alternate)
        {
            var artPhase = SwordMotionPhase(phase);
            return artPhase.HasValue
                ? StormInkFireSwordAuthoring.Load()?.PoseColumn(artPhase.Value, progress, alternate) ?? -1
                : -1;
        }

        internal static StormInkFireSwordMotionPhase? SwordMotionPhase(StormInkWeaponMotion.Phase phase) => phase switch
        {
            StormInkWeaponMotion.Phase.Ready => StormInkFireSwordMotionPhase.Windup,
            StormInkWeaponMotion.Phase.Strike => StormInkFireSwordMotionPhase.Strike,
            StormInkWeaponMotion.Phase.Follow => StormInkFireSwordMotionPhase.Follow,
            _ => null
        };

        static bool TryFireSwordHand(Sprite body, bool left, out Vector2 uv)
        {
            var authoring = StormInkFireSwordAuthoring.Load();
            if (authoring != null && authoring.TryHand(body, left, out uv)) return true;
            uv = default;
            return false;
        }
    }
}
