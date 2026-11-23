using Roguehalla.Net;

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
            if (!has) return AbilityFace.Empty;
            double left = Finite(readyAtMs) - Finite(nowMs);
            if (left > 0d)
            {
                float sweep = totalMs > 0d ? Clamp01(left / totalMs) : 1f;
                return new AbilityFace(AbilityPhase.Cooling, sweep, (int)System.Math.Ceiling(left / 1000d), charges, -1f);
            }
            return new AbilityFace(AbilityPhase.Ready, 0f, 0, charges, -1f);
        }

        /// <summary>The label the Wish tile shows with no Wish delivered.</summary>
        public const string NoWishLabel = "\u2014";

        /// <summary>The Wish's face. null: Empty. left = ReadyAt - now (NaN or infinite times count 0). left above 0: Cooling,
        /// Seconds = ceil(left / 1000) capped at int.MaxValue, Sweep = RechargeMs finite and above 0 ? clamp01(left / RechargeMs) : 1.
        /// Else Ready (the server's test: ready_at not after now). Badge = MaxCharges above 1 ? Charges : -1. Cast = no cast ? -1 :
        /// clamp01((now - StartedAt) / max(1, LandsAt - StartedAt)), in any phase.</summary>
        public static AbilityFace Wish(WishView? wish, double nowMs)
        {
            if (!wish.HasValue) return AbilityFace.Empty;
            var w = wish.Value;
            double now = Finite(nowMs);
            double left = Finite(w.ReadyAt) - now;
            int badge = w.MaxCharges > 1 ? w.Charges : -1;
            float cast = -1f;
            if (w.Cast.HasValue)
            {
                double started = Finite(w.Cast.Value.StartedAt);
                double span = System.Math.Max(1d, Finite(w.Cast.Value.LandsAt) - started);
                cast = Clamp01((now - started) / span);
            }
            if (left > 0d)
            {
                bool total = !double.IsNaN(w.RechargeMs) && !double.IsInfinity(w.RechargeMs) && w.RechargeMs > 0d;
                float sweep = total ? Clamp01(left / w.RechargeMs) : 1f;
                double sec = System.Math.Ceiling(left / 1000d);
                return new AbilityFace(AbilityPhase.Cooling, sweep, sec >= int.MaxValue ? int.MaxValue : (int)sec, badge, cast);
            }
            return new AbilityFace(AbilityPhase.Ready, 0f, 0, badge, cast);
        }

        /// <summary>abilityName, or NoWishLabel when wish is null or the name is null or empty.</summary>
        public static string WishLabel(WishView? wish, string abilityName)
        {
            return !wish.HasValue || string.IsNullOrEmpty(abilityName) ? NoWishLabel : abilityName;
        }

        static double Finite(double v) { return double.IsNaN(v) || double.IsInfinity(v) ? 0d : v; }

        static float Clamp01(double v) { return v < 0d ? 0f : v > 1d ? 1f : (float)v; }
    }

    /// <summary>Remembers how long a cooldown was when it was first seen, for abilities whose recharge time is not
    /// delivered. One per tile.</summary>
    public struct CooldownSpan
    {
        double _readyAtMs, _totalMs;
        bool _has;

        /// <summary>The length to divide by for this readyAt: the largest time left seen since readyAt last changed.
        /// 0 when nothing is left.</summary>
        public double Total(double readyAtMs, double nowMs)
        {
            double ready = double.IsNaN(readyAtMs) || double.IsInfinity(readyAtMs) ? 0d : readyAtMs;
            double now = double.IsNaN(nowMs) || double.IsInfinity(nowMs) ? 0d : nowMs;
            double left = ready - now;
            if (left <= 0d) { _has = false; _totalMs = 0d; return 0d; }
            if (!_has || ready != _readyAtMs) { _has = true; _readyAtMs = ready; _totalMs = left; }
            else if (left > _totalMs) _totalMs = left;
            return _totalMs;
        }
    }
}
