namespace Roguehalla.Net.Ui
{
    /// <summary>How far a world label is raised above its base to clear fighters: rises fast, holds, falls slowly.</summary>
    public struct LabelLift
    {
        public const float RiseUnitsPerSecond = 8f, FallUnitsPerSecond = 3f, HoldSeconds = .45f, Deadband = .03f;

        float _value, _target, _hold;

        /// <summary>The lift to draw this frame, in world units, never negative.</summary>
        public float Value { get { return _value; } }

        /// <summary>What <see cref="Value"/> moves toward.</summary>
        public float Target { get { return _target; } }

        /// <summary>First frame: Value = Target = max(0, wanted).</summary>
        public static LabelLift Start(float wanted)
        {
            float w = wanted > 0f ? wanted : 0f;
            return new LabelLift { _value = w, _target = w, _hold = HoldSeconds };
        }

        /// <summary>One frame later; returns the new state.</summary>
        public LabelLift Step(float wanted, float dt, bool reducedMotion)
        {
            if (wanted < 0f) wanted = 0f;
            if (dt < 0f) dt = 0f;
            var s = this;
            if (wanted > s._target + Deadband) { s._target = wanted; s._hold = HoldSeconds; }
            else if (wanted >= s._target - Deadband) s._hold = HoldSeconds;
            else
            {
                s._hold -= dt;
                if (s._hold <= 0f) { s._target = wanted; s._hold = HoldSeconds; }
            }
            if (reducedMotion) s._value = s._target;
            else if (s._target > s._value) s._value = System.Math.Min(s._target, s._value + RiseUnitsPerSecond * dt);
            else s._value = System.Math.Max(s._target, s._value - FallUnitsPerSecond * dt);
            return s;
        }
    }
}
