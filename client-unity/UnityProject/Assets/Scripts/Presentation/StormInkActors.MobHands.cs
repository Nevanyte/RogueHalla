using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Registers the painted palms for each facing of a regional creature.</summary>
    public static partial class StormInkActors
    {
        // Right palm, left palm. Each coordinate is a pixel centre within this particular painted pose.
        static readonly Hands[] BailiffPalms =
        {
            new Hands(39, 60, 88, 73), new Hands(44, 63, 81, 71), new Hands(42, 21, 91, 69), new Hands(34, 45, 92, 76), // S
            new Hands(35, 72, 94, 77), new Hands(34, 59, 92, 66), new Hands(56, 25, 99, 70), new Hands(101, 77, 47, 52), // SE
            new Hands(52, 71, 93, 64), new Hands(47, 66, 103, 63), new Hands(74, 36, 95, 63), new Hands(110, 69, 28, 56), // E
            new Hands(66, 72, 86, 52), new Hands(69, 77, 51, 55), new Hands(87, 24, 55, 57), new Hands(104, 45, 24, 58), // NE
            new Hands(94, 68, 43, 59), new Hands(83, 67, 48, 61), new Hands(91, 15, 36, 61), new Hands(85, 9, 37, 68), // N
            new Hands(97, 56, 36, 61), new Hands(90, 68, 36, 64), new Hands(74, 15, 29, 77), new Hands(17, 9, 99, 60), // NW
            new Hands(65, 53, 36, 55), new Hands(80, 67, 24, 71), new Hands(48, 40, 70, 52), new Hands(18, 45, 96, 62), // W
            new Hands(47, 81, 91, 59), new Hands(37, 79, 92, 69), new Hands(41, 35, 78, 76), new Hands(17, 49, 109, 58), // SW
        };

        internal static bool HasMobWeaponTrack(ActorKind kind, FactionId? faction, MobTypeId? mob, WeaponId? weapon) =>
            kind == ActorKind.RegionalMob && (((mob == MobTypeId.Bailiff || mob == MobTypeId.Executioner) && faction == FactionId.Sentenced && weapon == WeaponId.Sword) ||
                (mob == MobTypeId.AshDrake && faction == FactionId.Fire && weapon == WeaponId.Wand));

        public static bool TryMobHand(MobTypeId? mob, Sprite body, bool leftHand, out Vector2 uv)
        {
            uv = default;
            var palms = mob == MobTypeId.Bailiff ? BailiffPalms : mob == MobTypeId.Executioner ? ExecutionerPalms : null;
            if (palms == null || !MobPoseIndex(mob, body, out int index)) return false;
            uv = palms[index].UV(leftHand);
            return true;
        }

        static bool MobPoseIndex(MobTypeId? mob, Sprite body, out int index)
        {
            index = -1;
            if (!mob.HasValue || (uint)mob.Value >= Creatures.Length || body == null) return false;
            var sheet = Creatures[(int)mob.Value];
            if (sheet == null) return false;
            Rect rect = body.rect;
            int row = Rows - 1 - Mathf.RoundToInt(rect.y / rect.height);
            int col = Mathf.RoundToInt(rect.x / rect.width);
            index = row * Columns + col;
            return (uint)index < sheet.Poses.Length && sheet.Poses[index] == body;
        }
    }
}
