using System;

namespace Roguehalla.Net
{
    /// <summary>
    /// Per-frame motion for things the server sends at 20 Hz but the client draws at the display rate. Presentation only: no game rule,
    /// nothing here decides a hit, a position the server reads, or an outcome. Every new observation replaces what is drawn.
    /// </summary>
    public static class ProjectileGlide
    {
        /// <summary>Flight speeds in sim units per second, mirrored from rh-sim <c>content.rs</c> (Bow and Wand <c>Shape::Projectile</c>).</summary>
        public const float ArrowSpeed = 900f, BallSpeed = 380f;
        /// <summary>Longest a delivered flight is carried forward (s): a little more than one 50 ms tick, so a late packet does not stall
        /// the shot, and a lost one cannot carry it far from where the server has it.</summary>
        public const float MaxLeadSeconds = 0.065f;
        /// <summary>Step of the wall march (sim units): a quarter tile.</summary>
        public const float WallStep = 8f;

        public static float SpeedOf(ProjectileKind kind) { return kind == ProjectileKind.Ball ? BallSpeed : ArrowSpeed; }

        /// <summary>
        /// Where a flight delivered at (x, y) heading <paramref name="facing"/> (sim radians) is drawn <paramref name="age"/> seconds after
        /// it arrived. The lead is clamped to [0, <see cref="MaxLeadSeconds"/>]. With a <paramref name="sight"/> rule the drawn point
        /// stops at the last free spot before a sight-blocking tile, so a shot never pokes through the wall the server ends it on.
        /// Homing balls are carried along their delivered heading; the next observation corrects the turn.
        /// </summary>
        public static void Lead(ProjectileKind kind, float x, float y, float facing, float age, VisionRule sight, out float drawnX, out float drawnY)
        {
            drawnX = x; drawnY = y;
            if (!(age > 0f) || !IsFinite(x) || !IsFinite(y) || !IsFinite(facing)) return;
            float dist = SpeedOf(kind) * Math.Min(age, MaxLeadSeconds);
            float cx = MathF.Cos(facing), cy = MathF.Sin(facing);
            if (sight == null) { drawnX = x + cx * dist; drawnY = y + cy * dist; return; }
            float tile = (float)sight.Tile;
            if (sight.Blocked((long)MathF.Floor(x / tile), (long)MathF.Floor(y / tile))) return;
            float done = 0f;
            while (done < dist)
            {
                float next = Math.Min(done + WallStep, dist);
                float px = x + cx * next, py = y + cy * next;
                if (sight.Blocked((long)MathF.Floor(px / tile), (long)MathF.Floor(py / tile))) break;
                done = next;
            }
            drawnX = x + cx * done; drawnY = y + cy * done;
        }

        static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }

    /// <summary>Per-creature glide state (sim units, presenter clock in seconds).</summary>
    public struct GlideState
    {
        public float FromX, FromY, ToX, ToY, StartAt, Duration, LastTargetAt;
        public bool Primed;
        public void Reset() { this = default; }
    }

    /// <summary>
    /// Creatures are drawn from the newest observation, which moves them in 50 ms steps. A glide draws each new delivered position as
    /// a short slide from where the creature is drawn now, over the time the last observation took to arrive (about one tick), so the
    /// creature moves every frame and ends exactly on the delivered spot. It never runs past the delivered position, so it cannot show
    /// where a creature is going. A jump wider than <see cref="SnapUnits"/> (teleport, respawn, a renewed look) is drawn at once.
    /// </summary>
    public static class CreatureGlide
    {
        public const float NominalSeconds = 0.05f, MinSeconds = 0.03f, MaxSeconds = 0.1f;
        /// <summary>Two tiles: faster than any walk covers in one tick (Gait.TeleportSpeed 600 u/s is 30 u per tick).</summary>
        public const float SnapUnits = 64f;

        /// <summary>Notes the delivered position at time <paramref name="now"/>. Calling it again with the same position changes nothing.</summary>
        public static void Target(ref GlideState s, float x, float y, float now)
        {
            if (!s.Primed) { Snap(ref s, x, y, now); return; }
            if (x == s.ToX && y == s.ToY) return;
            Sample(in s, now, out float cx, out float cy);
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > SnapUnits * SnapUnits || !(now >= s.LastTargetAt)) { Snap(ref s, x, y, now); return; }
            float interval = now - s.LastTargetAt;
            s.Duration = interval > MaxSeconds ? NominalSeconds : Math.Max(MinSeconds, interval);
            s.FromX = cx; s.FromY = cy; s.ToX = x; s.ToY = y; s.StartAt = now; s.LastTargetAt = now;
        }

        /// <summary>Draws the delivered position at once and forgets the slide.</summary>
        public static void Snap(ref GlideState s, float x, float y, float now)
        {
            s.FromX = s.ToX = x; s.FromY = s.ToY = y; s.StartAt = now; s.LastTargetAt = now; s.Duration = NominalSeconds; s.Primed = true;
        }

        /// <summary>Where the creature is drawn at <paramref name="now"/>.</summary>
        public static void Sample(in GlideState s, float now, out float x, out float y)
        {
            float t = s.Duration > 0f ? (now - s.StartAt) / s.Duration : 1f;
            if (!(t < 1f)) { x = s.ToX; y = s.ToY; return; }
            if (t < 0f) t = 0f;
            x = s.FromX + (s.ToX - s.FromX) * t; y = s.FromY + (s.ToY - s.FromY) * t;
        }
    }
}
