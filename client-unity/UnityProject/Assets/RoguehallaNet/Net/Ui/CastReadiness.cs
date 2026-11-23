namespace Roguehalla.Net.Ui
{
    /// <summary>What a cast key would do now: <see cref="Empty"/> when the slot holds nothing, <see cref="Line"/> the reason it does
    /// nothing (null when the press goes through). STUB: P-UI-ABILITY-FEEDBACK-1.</summary>
    public readonly struct CastReading
    {
        /// <summary>The slot holds nothing.</summary>
        public readonly bool Empty;
        /// <summary>Why the key does nothing now; null when it goes.</summary>
        public readonly string Line;

        public CastReading(bool empty, string line) { Empty = empty; Line = line; }

        /// <summary>The slot holds something and nothing stops the press: the tile pops.</summary>
        public bool Pops { get { return false; } }
    }

    /// <summary>Why a cast key does nothing, from delivered facts (legacy abilitySlot.ts castDenial). STUB: every body returns a default
    /// until P-UI-ABILITY-FEEDBACK-1 lands.</summary>
    public static class CastReadiness
    {
        public const string LineKey = "", Casting = "", Stunned = "", Silenced = "", Rooted = "";
        public const ushort WishBlockers = 0;
        public const float HoldSeconds = 0f;

        public static CastReading Wish(WishView? wish, double nowMs, ushort effects) { return default(CastReading); }

        public static CastReading Spell(bool has, byte charges, double readyAtMs, double nowMs, ushort effects) { return default(CastReading); }

        public static CastReading Judge(AbilityFace face, bool chargeInHand, ushort effects, ushort blockedBy) { return default(CastReading); }

        public static string Cooldown(double seconds) { return ""; }
    }
}
