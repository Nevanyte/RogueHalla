using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The Haunted Executioner lifts its sword beside the hood and finishes the cut low.</summary>
    public static partial class StormInkActors
    {
        static readonly Hands[] ExecutionerPalms =
        {
            new Hands(32, 51, 81, 72), new Hands(44, 60, 92, 60), new Hands(37, 19, 96, 71), new Hands(40, 71, 89, 24), // S
            new Hands(39, 66, 95, 72), new Hands(43, 62, 106, 62), new Hands(50, 24, 106, 72), new Hands(101, 75, 50, 57), // SE
            new Hands(62, 66, 89, 53), new Hands(98, 59, 71, 48), new Hands(85, 33, 89, 52), new Hands(111, 58, 72, 51), // E
            new Hands(87, 68, 92, 49), new Hands(96, 75, 82, 60), new Hands(96, 25, 69, 50), new Hands(110, 38, 72, 50), // NE
            new Hands(97, 61, 45, 60), new Hands(88, 67, 40, 58), new Hands(94, 19, 35, 64), new Hands(91, 26, 39, 58), // N
            new Hands(94, 58, 28, 61), new Hands(98, 62, 28, 61), new Hands(74, 16, 26, 72), new Hands(9, 30, 100, 70), // NW
            new Hands(34, 58, 57, 61), new Hands(24, 70, 54, 53), new Hands(45, 15, 34, 71), new Hands(14, 49, 74, 71), // W
            new Hands(26, 59, 49, 72), new Hands(42, 60, 52, 68), new Hands(29, 15, 58, 78), new Hands(13, 53, 93, 58), // SW
        };

        internal static StormInkWeaponMotion.Pose ExecutionerCut(StormInkWeaponMotion.Pose pose,
            StormInkWeaponMotion.Phase phase, float progress, int row, bool reduced)
        {
            // A compact upward draw holds the blade above the shoulder. Contact folds the
            // armored torso forward; the final beat leaves the sword low before the guard returns.
            float side = row >= 5 ? -1f : 1f;
            float into = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            float extra = 0f, lean = 0f;
            switch (phase)
            {
                case StormInkWeaponMotion.Phase.Guard: extra = 14f; break;
                case StormInkWeaponMotion.Phase.Ready: extra = Mathf.Lerp(14f, 42f, into); lean = -5f * into; break;
                case StormInkWeaponMotion.Phase.Strike: extra = -39f; lean = 11f; break;
                case StormInkWeaponMotion.Phase.Follow: extra = Mathf.Lerp(-47f, 14f, into); lean = 9f * (1f - into); break;
            }
            return new StormInkWeaponMotion.Pose(pose.Grip, pose.Angle + side * extra, pose.Behind,
                pose.FlipFallback, reduced ? 0f : pose.BodyLean - side * lean, false, false);
        }
    }
}
