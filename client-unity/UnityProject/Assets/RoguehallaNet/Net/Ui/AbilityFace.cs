namespace Roguehalla.Net.Ui
{
    /// <summary>What an ability tile shows at one moment.</summary>
    public enum AbilityPhase { Empty, Ready, Cooling, Casting }

    /// <summary>The drawn state of one ability tile, computed from delivered facts.</summary>
    public readonly struct AbilityFace
    {
        /// <summary>What the tile shows.</summary>
        public readonly AbilityPhase Phase;
        /// <summary>Share of the tile the cooldown wedge covers, 0..1; 0 unless Cooling.</summary>
        public readonly float Sweep;
        /// <summary>Whole seconds left; 0 shows no number.</summary>
        public readonly int Seconds;
        /// <summary>Charge count to show; -1 shows no badge.</summary>
        public readonly int Badge;
        /// <summary>Cast progress 0..1; -1 shows no cast bar.</summary>
        public readonly float Cast;

        public AbilityFace(AbilityPhase phase, float sweep, int seconds, int badge, float cast)
        {
            Phase = phase; Sweep = sweep; Seconds = seconds; Badge = badge; Cast = cast;
        }

        /// <summary>Nothing in the slot: Empty, 0, 0, no badge, no cast bar.</summary>
        public static AbilityFace Empty { get { return new AbilityFace(AbilityPhase.Empty, 0f, 0, -1, -1f); } }
    }

    /// <summary>Faces of the ability tiles.</summary>
    public static class AbilityFaces
    {
        /// <summary>A quest spell slot. totalMs is the length of the running cooldown (from CooldownSpan); 0 or less
        /// draws a full wedge while cooling.</summary>
        public static AbilityFace Spell(bool has, int charges, double readyAtMs, double nowMs, double totalMs)
        {
            return default(AbilityFace);   // stub: P-UI-ABILITY-ROW-1 implements this
        }
    }

    /// <summary>Remembers how long a cooldown was when it was first seen, for abilities whose recharge time is not
    /// delivered. One per tile.</summary>
    public struct CooldownSpan
    {
        /// <summary>The length to divide by for this readyAt: the largest time left seen since readyAt last changed.
        /// 0 when nothing is left.</summary>
        public double Total(double readyAtMs, double nowMs)
        {
            return 0d;   // stub: P-UI-ABILITY-ROW-1 implements this
        }
    }
}
