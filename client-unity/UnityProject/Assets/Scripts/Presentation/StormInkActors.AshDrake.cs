using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The Ash Drake carries its wand in the jaw and throws its head forward on a confirmed cast.</summary>
    public static partial class StormInkActors
    {
        // On rear views the jaw is foreshortened between the horns and the body occludes the focus.
        static readonly Vector2Int[] DrakeFocus =
        {
            new Vector2Int(64, 74), new Vector2Int(64, 74), new Vector2Int(67, 93), new Vector2Int(84, 86), // S
            new Vector2Int(94, 58), new Vector2Int(95, 61), new Vector2Int(100, 82), new Vector2Int(106, 70), // SE
            new Vector2Int(111, 64), new Vector2Int(113, 66), new Vector2Int(110, 87), new Vector2Int(112, 69), // E
            new Vector2Int(104, 47), new Vector2Int(100, 50), new Vector2Int(105, 70), new Vector2Int(112, 43), // NE
            new Vector2Int(64, 48), new Vector2Int(64, 46), new Vector2Int(67, 62), new Vector2Int(68, 51), // N
            new Vector2Int(55, 41), new Vector2Int(47, 40), new Vector2Int(42, 58), new Vector2Int(21, 72), // NW
            new Vector2Int(17, 63), new Vector2Int(16, 66), new Vector2Int(16, 87), new Vector2Int(13, 73), // W
            new Vector2Int(24, 51), new Vector2Int(29, 48), new Vector2Int(25, 72), new Vector2Int(15, 45), // SW
        };

        public static bool TryCreatureFocus(MobTypeId? mob, Sprite body, out Vector2 uv)
        {
            uv = default;
            if (mob != MobTypeId.AshDrake || !MobPoseIndex(mob, body, out int index)) return false;
            var p = DrakeFocus[index];
            uv = new Vector2((p.x + .5f) / 128f, 1f - (p.y + .5f) / 128f);
            return true;
        }

        internal static StormInkWeaponMotion.Pose DrakeCast(StormInkWeaponMotion.Pose pose,
            StormInkWeaponMotion.Phase phase, float progress, int row, bool reduced)
        {
            // The drake retracts its muzzle, then thrusts the focus along its snout. A rear-facing
            // cast reads above the wings and a side cast follows the drawn snout through recovery.
            float aim = (2 - row) * Mathf.PI / 4f;
            float side = Mathf.Cos(aim);
            float pitch = phase == StormInkWeaponMotion.Phase.Ready ? -Mathf.SmoothStep(0, 1, progress) * 15f :
                phase == StormInkWeaponMotion.Phase.Strike ? 9f :
                phase == StormInkWeaponMotion.Phase.Follow ? 9f * (1f - Mathf.SmoothStep(0, 1, progress)) : 0;
            float angle = Mathf.Atan2(-Mathf.Sin(aim) * .72f, side) * Mathf.Rad2Deg - 90f;
            float carry = phase == StormInkWeaponMotion.Phase.Strike ? 0f :
                phase == StormInkWeaponMotion.Phase.Ready ? Mathf.Lerp(28f, 41f, Mathf.Clamp01(progress)) :
                phase == StormInkWeaponMotion.Phase.Follow ? 28f * Mathf.SmoothStep(0, 1, progress) : 28f;
            float sideSign = Mathf.Abs(side) < .1f ? 1f : Mathf.Sign(side);
            return new StormInkWeaponMotion.Pose(pose.Grip, angle - side * pitch + sideSign * carry,
                row >= 3 && row <= 5, false, reduced ? 0f : -side * pitch * .58f, false, false);
        }
    }
}
