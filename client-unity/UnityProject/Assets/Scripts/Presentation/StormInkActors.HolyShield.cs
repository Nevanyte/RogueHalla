using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Painted Holy shield actions and their two anatomical hand registrations.</summary>
    public static partial class StormInkActors
    {
        static Sheet _holyShield;

        // Right hand (held near belt), left hand (shield grip). Brace, press, recovery, cover.
        static readonly Hands[] HolyShieldPalms =
        {
            new Hands(47,65,88,58), new Hands(47,68,79,43), new Hands(48,68,87,63), new Hands(55,73,59,51), // S
            new Hands(55,71,94,55), new Hands(50,67,101,46), new Hands(57,69,86,64), new Hands(55,74,93,52), // SE
            new Hands(64,66,91,51), new Hands(64,67,105,44), new Hands(64,64,81,60), new Hands(65,68,94,50), // E
            new Hands(61,65,85,47), new Hands(67,69,93,43), new Hands(64,64,80,53), new Hands(72,63,91,47), // NE
            new Hands(83,56,41,54), new Hands(83,58,22,38), new Hands(97,56,42,57), new Hands(100,56,42,41), // N
            new Hands(78,58,41,55), new Hands(80,63,14,39), new Hands(93,59,48,51), new Hands(96,60,34,43), // NW
            new Hands(68,64,36,47), new Hands(75,60,15,41), new Hands(69,63,47,60), new Hands(83,66,37,43), // W
            new Hands(55,66,45,47), new Hands(69,66,18,40), new Hands(45,67,58,64), new Hands(49,67,50,44), // SW
        };

        internal static Sprite HolyShieldFrame(ActorKind kind, FactionId? faction, WeaponId? weapon,
            int direction, StormInkWeaponMotion.Phase phase)
        {
            if (kind != ActorKind.Player || faction != FactionId.Holy || weapon != WeaponId.Shield) return null;
            int pose = phase switch
            {
                StormInkWeaponMotion.Phase.Ready => 0,
                StormInkWeaponMotion.Phase.Strike => 1,
                StormInkWeaponMotion.Phase.Follow => 2,
                StormInkWeaponMotion.Phase.Block => 3,
                _ => -1
            };
            if (pose < 0) return null;
            _holyShield ??= Load("HolyShield");
            return _holyShield.Poses[PoseRow(direction) * Columns + pose];
        }

        static bool TryHolyShieldHand(Sprite body, int index, bool left, out Vector2 uv)
        {
            uv = default;
            if (_holyShield?.Poses[index] != body) return false;
            uv = HolyShieldPalms[index].UV(left);
            return true;
        }
    }
}
