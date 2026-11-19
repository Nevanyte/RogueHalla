using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    public class MotionSmoothingTests
    {
        const float East = 0f, South = MathF.PI / 2;

        [Test]
        public void Arrow_is_carried_along_its_heading_at_bow_speed()
        {
            ProjectileGlide.Lead(ProjectileKind.Arrow, 100f, 200f, East, 0.025f, null, out float x, out float y);
            Assert.AreEqual(100f + 900f * 0.025f, x, 1e-3f); Assert.AreEqual(200f, y, 1e-3f);
            ProjectileGlide.Lead(ProjectileKind.Ball, 100f, 200f, South, 0.05f, null, out x, out y);
            Assert.AreEqual(100f, x, 1e-3f); Assert.AreEqual(200f + 380f * 0.05f, y, 1e-3f);
        }

        [Test]
        public void Lead_is_clamped_and_never_runs_backwards()
        {
            ProjectileGlide.Lead(ProjectileKind.Arrow, 0f, 0f, East, 1f, null, out float x, out _);
            Assert.AreEqual(900f * ProjectileGlide.MaxLeadSeconds, x, 1e-3f);
            ProjectileGlide.Lead(ProjectileKind.Arrow, 50f, 60f, East, -0.02f, null, out x, out float y);
            Assert.AreEqual(50f, x); Assert.AreEqual(60f, y);
            ProjectileGlide.Lead(ProjectileKind.Arrow, 50f, 60f, float.NaN, 0.02f, null, out x, out y);
            Assert.AreEqual(50f, x); Assert.AreEqual(60f, y);
        }

        [Test]
        public void Lead_stops_before_a_sight_blocking_wall()
        {
            var tiles = new byte[10 * 10];
            for (int ty = 0; ty < 10; ty++) tiles[ty * 10 + 5] = 1;     // wall column at x 160..192
            var rule = new VisionRule(new MapBlob(10, 10, tiles));
            // From x 150 heading east, 58 units of lead would end inside the wall; the shot stops short of x 160.
            ProjectileGlide.Lead(ProjectileKind.Arrow, 150f, 48f, East, 0.065f, rule, out float x, out float y);
            Assert.Less(x, 160f); Assert.GreaterOrEqual(x, 150f); Assert.AreEqual(48f, y, 1e-3f);
            // Open ground: the full lead.
            ProjectileGlide.Lead(ProjectileKind.Arrow, 40f, 48f, East, 0.05f, rule, out x, out _);
            Assert.AreEqual(40f + 45f, x, 1e-3f);
        }

        [Test]
        public void Creature_glides_from_where_it_is_drawn_to_the_new_spot_in_about_one_tick()
        {
            var s = default(GlideState);
            CreatureGlide.Target(ref s, 0f, 0f, 1.00f);
            CreatureGlide.Sample(in s, 1.00f, out float x, out _); Assert.AreEqual(0f, x);
            CreatureGlide.Target(ref s, 10f, 0f, 1.05f);      // next observation, one tick later
            CreatureGlide.Sample(in s, 1.05f, out x, out _); Assert.AreEqual(0f, x, 1e-4f);
            CreatureGlide.Sample(in s, 1.075f, out x, out _); Assert.AreEqual(5f, x, 1e-3f);
            CreatureGlide.Sample(in s, 1.10f, out x, out _); Assert.AreEqual(10f, x, 1e-4f);
            CreatureGlide.Sample(in s, 2.00f, out x, out _); Assert.AreEqual(10f, x, 1e-4f);   // never past the delivered spot
            // A repeat of the same spot (Present runs twice a frame) does not restart the slide.
            CreatureGlide.Target(ref s, 20f, 0f, 1.10f);
            CreatureGlide.Target(ref s, 20f, 0f, 1.12f);
            CreatureGlide.Sample(in s, 1.125f, out x, out _); Assert.AreEqual(15f, x, 1e-3f);
        }

        [Test]
        public void Creature_jump_wider_than_two_tiles_is_drawn_at_once()
        {
            var s = default(GlideState);
            CreatureGlide.Target(ref s, 0f, 0f, 1f);
            CreatureGlide.Target(ref s, 200f, 0f, 1.05f);
            CreatureGlide.Sample(in s, 1.05f, out float x, out _); Assert.AreEqual(200f, x);
        }

        [Test]
        public void Slide_time_follows_arrival_spacing_within_bounds()
        {
            var s = default(GlideState);
            CreatureGlide.Target(ref s, 0f, 0f, 1f);
            CreatureGlide.Target(ref s, 10f, 0f, 1.005f);      // a bunched packet
            Assert.AreEqual(CreatureGlide.MinSeconds, s.Duration, 1e-6f);
            CreatureGlide.Target(ref s, 20f, 0f, 2f);          // after a long gap
            Assert.AreEqual(CreatureGlide.NominalSeconds, s.Duration, 1e-6f);
        }
    }
}
