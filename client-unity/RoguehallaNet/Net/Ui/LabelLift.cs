namespace Roguehalla.Net.Ui
{
    /// <summary>How far a world label is raised above its base to clear fighters: rises fast, holds, falls slowly.</summary>
    public struct LabelLift
    {
        public const float RiseUnitsPerSecond = 8f, FallUnitsPerSecond = 3f, HoldSeconds = .45f, Deadband = .03f;

        /// <summary>The lift to draw this frame, in world units, never negative.</summary>
        public float Value { get { return 0f; } }

        /// <summary>What <see cref="Value"/> moves toward.</summary>
        public float Target { get { return 0f; } }

        /// <summary>First frame: Value = Target = max(0, wanted). Stub: does nothing yet.</summary>
        public static LabelLift Start(float wanted) { return default(LabelLift); }

        /// <summary>One frame later; returns the new state. Stub: does nothing yet.</summary>
        public LabelLift Step(float wanted, float dt, bool reducedMotion) { return this; }
    }
}
