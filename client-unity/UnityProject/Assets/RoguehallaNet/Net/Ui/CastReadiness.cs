namespace Roguehalla.Net.Ui
{
    /// <summary>What a cast key would do now: <see cref="Empty"/> when the slot holds nothing, <see cref="Line"/> the reason it does
    /// nothing (null when the press goes through).</summary>
    public readonly struct CastReading
    {
        /// <summary>The slot holds nothing.</summary>
        public readonly bool Empty;
        /// <summary>Why the key does nothing now; null when it goes.</summary>
        public readonly string Line;

        public CastReading(bool empty, string line) { Empty = empty; Line = line; }

        /// <summary>The slot holds something and nothing stops the press: the tile pops.</summary>
        public bool Pops { get { return !Empty && Line == null; } }
    }

    /// <summary>Why a cast key does nothing, from delivered facts (legacy abilitySlot.ts castDenial).</summary>
    public static class CastReadiness
    {
        public const string LineKey = "cast", Casting = "Casting", Stunned = "Stunned", Silenced = "Silenced", Rooted = "Rooted";
        public const ushort WishBlockers = (ushort)(Indicators.Stunned | Indicators.Silenced);
        public const float HoldSeconds = 1f;

        /// <summary>The Wish key: cooldown, cast in progress, stun and silence.</summary>
        public static CastReading Wish(WishView? wish, double nowMs, ushort effects)
        {
            return Judge(AbilityFaces.Wish(wish, nowMs), false, effects, WishBlockers);
        }

        /// <summary>A quest spell key: casting and cooldown only.</summary>
        public static CastReading Spell(bool has, byte charges, double readyAtMs, double nowMs, ushort effects)
        {
            return Judge(AbilityFaces.Spell(has, charges, readyAtMs, nowMs, 0d), charges > 0, effects, 0);
        }

        /// <summary>Legacy castDenial order: empty, casting, cooling, then a blocker.</summary>
        public static CastReading Judge(AbilityFace face, bool chargeInHand, ushort effects, ushort blockedBy)
        {
            if (face.Phase == AbilityPhase.Empty) return new CastReading(true, null);
            if (face.Cast >= 0f || (effects & Indicators.Casting) != 0) return new CastReading(false, Casting);
            if (face.Phase == AbilityPhase.Cooling && !chargeInHand) return new CastReading(false, "Ready in " + Cooldown(face.Seconds));
            ushort hit = (ushort)(effects & blockedBy);
            if ((hit & Indicators.Stunned) != 0) return new CastReading(false, Stunned);
            if ((hit & Indicators.Silenced) != 0) return new CastReading(false, Silenced);
            if ((hit & Indicators.Rooted) != 0) return new CastReading(false, Rooted);
            return new CastReading(false, null);
        }

        /// <summary>Legacy formatCooldown: whole seconds up, m:ss from a minute.</summary>
        public static string Cooldown(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d) seconds = 0d;
            double c = System.Math.Ceiling(seconds);
            long s = c >= int.MaxValue ? int.MaxValue : (long)c;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (s < 60) return s.ToString(inv);
            long r = s % 60;
            return (s / 60).ToString(inv) + ":" + (r < 10 ? "0" : "") + r.ToString(inv);
        }
    }
}
