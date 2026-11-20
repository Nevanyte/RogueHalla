using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Eight painted Fire axe bearings and their two anatomical hand sockets.</summary>
    public static partial class StormInkActors
    {
        const int FireAxeColumns = 5;
        static Sheet _fireAxe;

        // Right axe grip, left counterbalance, in coil, cut, recovery, guard and stride order.
        static readonly Hands[] FireAxePalms =
        {
            new Hands(38,66,87,80), new Hands(60,63,87,58), new Hands(41,90,86,67), new Hands(35,78,90,63), new Hands(38,73,90,65), // S
            new Hands(39,62,85,79), new Hands(79,68,87,54), new Hands(65,85,88,65), new Hands(41,76,82,60), new Hands(38,75,87,62), // SE
            new Hands(56,67,85,78), new Hands(105,64,45,51), new Hands(85,87,43,59), new Hands(62,76,43,60), new Hands(44,74,87,65), // E
            new Hands(78,72,41,74), new Hands(104,61,32,57), new Hands(91,84,38,70), new Hands(76,75,37,68), new Hands(76,73,41,70), // NE
            new Hands(86,72,40,80), new Hands(97,33,32,69), new Hands(94,90,37,75), new Hands(93,80,35,74), new Hands(94,71,38,70), // N
            new Hands(95,81,26,71), new Hands(13,69,110,68), new Hands(35,92,94,76), new Hands(94,76,38,76), new Hands(91,70,38,68), // NW
            new Hands(88,76,25,81), new Hands(13,75,107,64), new Hands(25,93,102,70), new Hands(37,75,95,70), new Hands(84,68,39,66), // W
            new Hands(91,74,29,86), new Hands(13,65,100,60), new Hands(29,92,96,64), new Hands(27,81,97,66), new Hands(33,71,91,68), // SW
        };

        internal static Sprite FireAxeFrame(ActorKind kind, FactionId? faction, WeaponId? weapon,
            int direction, StormInkWeaponMotion.Phase phase, bool stride, int stage = 0, bool alternate = false)
        {
            if (kind != ActorKind.Player || faction != FactionId.Fire || weapon != WeaponId.Axe) return null;
            var authoring = StormInkFireAxeAuthoring.Load();
            if (authoring != null)
            {
                var authoredPose = phase switch
                {
                    StormInkWeaponMotion.Phase.Ready => StormInkFireAxePose.Windup,
                    StormInkWeaponMotion.Phase.Strike => alternate ? StormInkFireAxePose.ReleaseB : StormInkFireAxePose.ReleaseA,
                    StormInkWeaponMotion.Phase.Follow => StormInkFireAxePose.Recovery,
                    _ => stride ? StormInkFireAxePose.Stride : StormInkFireAxePose.Guard,
                };
                var authoredFrame = authoring.Frame(PoseRow(direction), authoredPose, stage);
                if (authoredFrame != null) return authoredFrame;
            }

            int pose = phase switch
            {
                StormInkWeaponMotion.Phase.Ready => 0,
                StormInkWeaponMotion.Phase.Strike => 1,
                StormInkWeaponMotion.Phase.Follow => 2,
                _ => stride ? 4 : 3,
            };
            _fireAxe ??= Load("FireAxe", FireAxeColumns);
            return _fireAxe.Poses[PoseRow(direction) * FireAxeColumns + pose];
        }

        static bool TryFireAxeHand(Sprite body, int row, int column, bool left, out Vector2 uv)
        {
            if (StormInkFireAxeAuthoring.Load()?.TryHand(body, left, out uv) == true) return true;
            uv = default;
            if ((uint)column >= FireAxeColumns || (uint)row >= Rows) return false;
            int index = row * FireAxeColumns + column;
            if (_fireAxe?.Poses[index] != body) return false;
            uv = FireAxePalms[index].UV(left);
            return true;
        }
    }
}
