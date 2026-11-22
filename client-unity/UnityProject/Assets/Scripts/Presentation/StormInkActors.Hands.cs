using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Maps each champion pose to its two anatomical palms.</summary>
    public static partial class StormInkActors
    {
        readonly struct Hands
        {
            readonly byte _rx, _ry, _lx, _ly;
            public Hands(byte rx, byte ry, byte lx, byte ly) { _rx = rx; _ry = ry; _lx = lx; _ly = ly; }

            // Painted coordinates are pixel centres measured from the top left of a 128px cell.
            public Vector2 UV(bool left) => new Vector2(((left ? _lx : _rx) + .5f) / 128f,
                1f - ((left ? _ly : _ry) + .5f) / 128f);
        }

        // Each facing has four independently registered hand pairs in ready, stride, windup and strike order.
        static readonly Hands[][] PaintedHands =
        {
            new Hands[] // FireChampion
            {
                new Hands(33, 59, 94, 75), new Hands(42, 76, 95, 80), new Hands(35, 18, 99, 72), new Hands(39, 84, 83, 22), // S
                new Hands(29, 72, 93, 75), new Hands(31, 67, 100, 66), new Hands(34, 19, 99, 68), new Hands(112, 67, 22, 73), // SE
                new Hands(27, 78, 96, 57), new Hands(25, 74, 106, 54), new Hands(72, 42, 100, 63), new Hands(113, 51, 12, 58), // E
                new Hands(96, 60, 27, 61), new Hands(94, 80, 30, 72), new Hands(87, 18, 15, 77), new Hands(112, 45, 15, 58), // NE
                new Hands(98, 67, 30, 78), new Hands(94, 74, 33, 72), new Hands(99, 15, 19, 66), new Hands(92, 32, 29, 70), // N
                new Hands(97, 60, 24, 57), new Hands(100, 66, 23, 68), new Hands(89, 15, 14, 82), new Hands(11, 52, 103, 54), // NW
                new Hands(20, 58, 95, 72), new Hands(100, 58, 17, 72), new Hands(70, 11, 21, 80), new Hands(10, 48, 105, 55), // W
                new Hands(94, 68, 21, 61), new Hands(94, 73, 18, 73), new Hands(40, 16, 80, 82), new Hands(10, 54, 105, 46), // SW
            },
            new Hands[] // SentencedChampion
            {
                new Hands(34, 53, 80, 67), new Hands(51, 67, 89, 67), new Hands(37, 21, 99, 72), new Hands(50, 76, 93, 30), // S
                new Hands(34, 68, 96, 68), new Hands(45, 61, 108, 62), new Hands(61, 27, 107, 63), new Hands(101, 74, 43, 59), // SE
                new Hands(53, 69, 99, 54), new Hands(54, 71, 100, 64), new Hands(83, 33, 93, 52), new Hands(109, 66, 38, 51), // E
                new Hands(95, 51, 75, 45), new Hands(75, 79, 92, 53), new Hands(98, 26, 81, 53), new Hands(110, 41, 30, 53), // NE
                new Hands(93, 66, 41, 61), new Hands(85, 69, 47, 60), new Hands(89, 21, 36, 57), new Hands(83, 32, 35, 66), // N
                new Hands(97, 59, 36, 56), new Hands(86, 57, 30, 59), new Hands(79, 14, 28, 69), new Hands(49, 52, 57, 60), // NW
                new Hands(51, 50, 33, 57), new Hands(24, 75, 48, 79), new Hands(53, 15, 31, 82), new Hands(14, 51, 85, 69), // W
                new Hands(47, 40, 38, 57), new Hands(43, 49, 39, 73), new Hands(35, 13, 68, 80), new Hands(13, 60, 103, 43), // SW
            },
            new Hands[] // PlagueChampion
            {
                new Hands(34, 56, 94, 78), new Hands(41, 59, 95, 72), new Hands(33, 15, 99, 70), new Hands(34, 77, 93, 35), // S
                new Hands(28, 66, 94, 70), new Hands(25, 62, 104, 59), new Hands(34, 15, 103, 68), new Hands(112, 54, 22, 71), // SE
                new Hands(20, 78, 94, 58), new Hands(16, 80, 104, 57), new Hands(76, 48, 24, 72), new Hands(111, 55, 16, 69), // E
                new Hands(99, 59, 20, 74), new Hands(101, 74, 23, 72), new Hands(92, 21, 12, 76), new Hands(110, 44, 18, 59), // NE
                new Hands(98, 64, 29, 70), new Hands(96, 76, 34, 75), new Hands(99, 14, 19, 68), new Hands(92, 30, 28, 74), // N
                new Hands(99, 60, 23, 56), new Hands(103, 70, 24, 68), new Hands(88, 12, 14, 78), new Hands(12, 54, 101, 60), // NW
                new Hands(17, 60, 92, 70), new Hands(98, 59, 16, 74), new Hands(75, 13, 19, 82), new Hands(10, 48, 104, 56), // W
                new Hands(90, 70, 14, 60), new Hands(99, 72, 17, 72), new Hands(28, 14, 96, 78), new Hands(10, 56, 108, 48), // SW
            },
            new Hands[] // FrostChampion
            {
                new Hands(32, 65, 96, 76), new Hands(31, 70, 94, 70), new Hands(33, 22, 100, 80), new Hands(39, 48, 91, 32), // S
                new Hands(30, 70, 96, 70), new Hands(34, 64, 98, 62), new Hands(31, 18, 99, 69), new Hands(106, 58, 25, 62), // SE
                new Hands(68, 76, 95, 59), new Hands(72, 80, 103, 54), new Hands(57, 34, 99, 64), new Hands(103, 52, 10, 58), // E
                new Hands(95, 68, 22, 70), new Hands(99, 78, 25, 74), new Hands(94, 19, 19, 80), new Hands(110, 49, 14, 59), // NE
                new Hands(98, 67, 29, 57), new Hands(96, 79, 33, 78), new Hands(100, 16, 20, 68), new Hands(94, 30, 31, 72), // N
                new Hands(99, 64, 21, 56), new Hands(101, 68, 22, 70), new Hands(88, 15, 13, 80), new Hands(10, 56, 102, 55), // NW
                new Hands(30, 57, 97, 68), new Hands(98, 59, 24, 76), new Hands(73, 13, 18, 77), new Hands(12, 51, 105, 60), // W
                new Hands(93, 70, 30, 60), new Hands(95, 75, 28, 70), new Hands(40, 18, 80, 82), new Hands(19, 55, 103, 47), // SW
            },
            new Hands[] // HolyChampion
            {
                new Hands(33, 56, 97, 72), new Hands(37, 57, 99, 67), new Hands(35, 16, 99, 72), new Hands(42, 57, 88, 31), // S
                new Hands(31, 67, 92, 66), new Hands(31, 64, 101, 61), new Hands(34, 19, 99, 69), new Hands(110, 59, 21, 53), // SE
                new Hands(38, 70, 95, 57), new Hands(29, 79, 100, 56), new Hands(88, 46, 48, 76), new Hands(109, 54, 10, 68), // E
                new Hands(93, 66, 27, 63), new Hands(99, 75, 20, 70), new Hands(88, 19, 17, 76), new Hands(112, 45, 14, 57), // NE
                new Hands(97, 68, 33, 60), new Hands(93, 78, 34, 77), new Hands(99, 16, 20, 65), new Hands(93, 31, 27, 71), // N
                new Hands(97, 60, 25, 58), new Hands(100, 66, 25, 68), new Hands(87, 13, 16, 79), new Hands(11, 51, 103, 56), // NW
                new Hands(21, 58, 98, 69), new Hands(99, 65, 22, 74), new Hands(74, 12, 17, 76), new Hands(11, 48, 103, 56), // W
                new Hands(94, 75, 32, 65), new Hands(100, 76, 31, 74), new Hands(36, 15, 88, 80), new Hands(13, 54, 100, 45), // SW
            },
        };

        public static bool TryHand(FactionId? faction, Sprite body, bool leftHand, out Vector2 uv)
        {
            uv = default;
            if (!faction.HasValue || body == null) return false;
            int id = (int)faction.Value;
            if ((uint)id >= PaintedHands.Length || PaintedHands[id] == null || Champions[id] == null) return false;
            Rect rect = body.rect;
            int column = Mathf.RoundToInt(rect.x / rect.width);
            int row = Rows - 1 - Mathf.RoundToInt(rect.y / rect.height);
            if (faction == FactionId.Fire && TryFireBowHand(body, leftHand, out uv)) return true;
            if (faction == FactionId.Fire && TryFireSwordHand(body, leftHand, out uv)) return true;
            if (faction == FactionId.Fire && TryFireAxeHand(body, row, column, leftHand, out uv)) return true;
            if ((uint)column >= Columns || (uint)row >= Rows) return false;
            int index = row * Columns + column;
            if (faction == FactionId.Holy && TryHolyShieldHand(body, index, leftHand, out uv)) return true;
            if (Champions[id].Poses[index] != body) return false;
            uv = PaintedHands[id][index].UV(leftHand);
            return true;
        }
    }
}
