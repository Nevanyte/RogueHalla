using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-TICK-4 oracle (C#, ADR 0029): the walk and attack-pause prediction against the Rust vectors at BOTH step lengths.
    /// <c>protocol/fixtures/server_walk_v3.json</c> and <c>ranged_pause_v3.json</c> carry <c>runs</c>: one per <c>tickMs</c>
    /// (50, then 25), each the v2 case list replayed by a sim of that step (the same wall time: twice the rows at 25).
    /// The client reads the rate from its Welcome: <see cref="LocalMotion.StepMs"/> is set to the run's step before any row is fed.
    /// The v2 tests (HitboxPredictionTests, RangedPausePredictionTests) stay as they are and pin 50 ms through the legacy consts.
    /// </summary>
    public class TickRateVectorTests
    {
        const double WalkTol = 0.01;
        const uint Me = 3;

        static JsonDocument Read(string name)
        {
            return JsonDocument.Parse(File.ReadAllText(Path.Combine(Util.RepoRoot(), "protocol", "fixtures", name)));
        }

        static MapBlob MapOf(string fixture)
        {
            using var doc = Read(fixture);
            var reader = new Reader(Util.FromHex(doc.RootElement.GetProperty("mapBlob").GetProperty("hex").GetString()));
            var map = MapBlob.ReadCollisionFixture(ref reader);
            reader.End();
            return map;
        }

        static JsonElement RunAt(JsonElement root, double tickMs)
        {
            foreach (var run in root.GetProperty("runs").EnumerateArray())
                if (run.GetProperty("tickMs").GetDouble() == tickMs) return run;
            throw new ArgumentException("no run at " + tickMs + " ms");
        }

        static readonly double[] Rates = { 50.0, 25.0 };

        [Test]
        public void BothVectorFilesCarryAFiftyAndATwentyFiveMillisecondRun()
        {
            foreach (var (file, format) in new[] { ("server_walk_v3.json", "rh-server-walk/3"), ("ranged_pause_v3.json", "rh-ranged-pause/3") })
            {
                using var doc = Read(file);
                Assert.AreEqual(format, doc.RootElement.GetProperty("format").GetString(), file);
                var got = new List<double>();
                foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray()) got.Add(run.GetProperty("tickMs").GetDouble());
                CollectionAssert.AreEqual(Rates, got, file + ": the runs, in order");
            }
        }

        [TestCase(50.0, 241)]
        [TestCase(25.0, 481)]
        public void CornerWalksMatchTheServerPerStepAtTheRunsRate(double tickMs, int rows)
        {
            using var walk = Read("server_walk_v3.json");
            var root = walk.RootElement;
            var run = RunAt(root, tickMs);
            var foot = Footprint.Box(root.GetProperty("footprint").GetProperty("hx").GetDouble(), root.GetProperty("footprint").GetProperty("hy").GetDouble());
            byte archetype = (byte)root.GetProperty("archetype").GetInt32();
            var map = MapOf("pathfinding_v2.json");
            int cases = 0;
            double maxError = 0;
            foreach (var one in run.GetProperty("cases").EnumerateArray())
            {
                string name = one.GetProperty("name").GetString();
                var ticks = one.GetProperty("ticks");
                Assert.AreEqual(rows, ticks.GetArrayLength(), name + ": 12 s of steps");
                var motion = new LocalMotion(map) { Foot = foot, StepMs = tickMs };
                Assert.AreEqual(tickMs, motion.StepMs, "the step is the session's");
                var first = ticks[0];
                motion.OnSnapshot(Self(first.GetProperty("x").GetDouble(), first.GetProperty("y").GetDouble(), (byte)first.GetProperty("moveSeq").GetInt32(), archetype), 0.0);
                motion.Order((float)one.GetProperty("goal")[0].GetDouble(), (float)one.GetProperty("goal")[1].GetDouble(), 1, 0.0);
                for (int k = 1; k < ticks.GetArrayLength(); k++)
                {
                    double now = tickMs * k;
                    motion.Step(now);
                    double sx = ticks[k].GetProperty("x").GetDouble(), sy = ticks[k].GetProperty("y").GetDouble();
                    motion.OnSnapshot(Self(sx, sy, (byte)ticks[k].GetProperty("moveSeq").GetInt32(), archetype), now);
                    double err = Math.Sqrt(Math.Pow(motion.DrawnX - sx, 2) + Math.Pow(motion.DrawnY - sy, 2));
                    maxError = Math.Max(maxError, err);
                    Assert.LessOrEqual(err, WalkTol, name + " step " + k + " at " + tickMs + " ms");
                }
                Assert.AreEqual(0, motion.Snaps, name + ": no snap");
                Assert.AreEqual(0, motion.ExpiredOrders, name);
                cases++;
            }
            Assert.GreaterOrEqual(cases, 3, "the corner walks");
            TestContext.Progress.WriteLine("server_walk_v3 @" + tickMs + " ms: " + cases + " cases, max |drawn - server| " + maxError.ToString("E3") + " u");
        }

        static WireSelf Self(double x, double y, byte moveSeq, byte archetype)
        {
            return new WireSelf { Status = Wire.DefaultStatus, Id = 3, X = (float)x, Y = (float)y, MoveSeq = moveSeq, Hp = 10, MaxHp = 10, Archetype = archetype };
        }

        // ----------------------------------------------------------------------------------------------------------------
        // The attack pause (the Place check of RangedPausePredictionTests, per run)

        static WireOwnerCombat OwnerAt(JsonElement row)
        {
            var o = row.GetProperty("owner");
            if (o.ValueKind != JsonValueKind.Object) return null;
            return new WireOwnerCombat
            {
                DrawStartedAt = o.GetProperty("drawStartedAt").GetUInt32(), ReadyAt = o.GetProperty("readyAt").GetUInt32(),
                AsMilli = o.GetProperty("asMilli").GetDouble(), WindupMilli = o.GetProperty("windupMilli").GetDouble(), SpeedMilli = o.GetProperty("speedMilli").GetDouble(),
            };
        }

        static WireSelf SelfAt(JsonElement row)
        {
            return new WireSelf
            {
                Id = Me, X = (float)row.GetProperty("x").GetDouble(), Y = (float)row.GetProperty("y").GetDouble(), Hp = 10, MaxHp = 10, Status = 1,
                MoveSeq = 0, Weapon = (byte)row.GetProperty("weapon").GetInt32(), Effects = (ushort)row.GetProperty("effects").GetInt32(),
            };
        }

        /// <summary>(row, the server stopped the step, the mirror stops the step ending at that row's server time), rows with a defined multiplier.</summary>
        static List<(int row, bool server, bool mirror)> Place(JsonElement one, double tickMs, MapBlob map)
        {
            var rows = one.GetProperty("rows");
            var m = new LocalMotion(map) { StepMs = tickMs };
            var result = new List<(int, bool, bool)>();
            for (int k = 0; k < rows.GetArrayLength(); k++)
            {
                var row = rows[k];
                double tick = row.GetProperty("t").GetDouble();
                foreach (var fx in row.GetProperty("fx").EnumerateArray())
                {
                    switch (fx.GetProperty("kind").GetString())
                    {
                        case "windup": m.NoteOwnWindup(tick, fx.GetProperty("ms").GetInt32(), (byte)fx.GetProperty("weapon").GetInt32()); break;
                        case "stagger": m.NoteOwnStagger(tick); break;
                        case "cancel": m.NoteOwnCancel(tick); break;
                    }
                }
                m.OnSnapshot(SelfAt(row), tick, null, tick);
                var own = OwnerAt(row);
                if (own != null) m.NoteOwnerDraw(tick, own.DrawStartedAt, own.ReadyAt, own.WindupMilli);
                var mult = row.GetProperty("moveMult");
                if (k > 0 && mult.ValueKind == JsonValueKind.Number) result.Add((k, mult.GetInt32() == 0, m.AttackPauseStops(tick)));
            }
            return result;
        }

        static readonly string[] AllCases =
        {
            "wand-single-shot", "wand-held", "wand-rejected-cooldown", "wand-staggered", "wand-staggered-in-pause",
            "wand-weapon-swap", "bow-single-shot", "bow-held", "bow-holy-faction", "bow-plague-faction", "bow-frost-faction",
            "bow-rejected-cooldown", "bow-staggered-mid-draw", "bow-weapon-swap-mid-draw", "bow-weapon-swap-early-draw", "sword-swing",
        };

        [TestCase(50.0)]
        [TestCase(25.0)]
        public void TheMirrorStopsExactlyTheStepsTheServerStoppedAtTheRunsRate(double tickMs)
        {
            using var doc = Read("ranged_pause_v3.json");
            var run = RunAt(doc.RootElement, tickMs);
            var map = MapOf("pathfinding_v1.json");
            var seen = new HashSet<string>();
            int casesWithPause = 0;
            foreach (var one in run.GetProperty("cases").EnumerateArray())
            {
                string name = one.GetProperty("name").GetString();
                seen.Add(name);
                var placed = Place(one, tickMs, map);
                int stopped = 0;
                foreach (var p in placed)
                {
                    Assert.AreEqual(p.server, p.mirror, name + " @" + tickMs + " ms: row " + p.row + " the server " + (p.server ? "stopped" : "walked") + " but the mirror " + (p.mirror ? "stops" : "walks"));
                    if (p.server) stopped++;
                }
                if (stopped > 0) casesWithPause++;
            }
            CollectionAssert.AreEquivalent(AllCases, seen, "the same sixteen cases at every rate");
            Assert.GreaterOrEqual(casesWithPause, 10, "the vectors must exercise the pause at " + tickMs + " ms");
        }

        [Test]
        public void TheFiftyMillisecondRunStopsTheSameStepsAsTheV2File()
        {
            // The v3 50 ms run IS the v2 file: a fresh Place over both gives the same stop rows.
            using var v2 = Read("ranged_pause_v2.json");
            using var v3 = Read("ranged_pause_v3.json");
            var map = MapOf("pathfinding_v1.json");
            var run = RunAt(v3.RootElement, 50.0);
            var by3 = new Dictionary<string, JsonElement>();
            foreach (var c in run.GetProperty("cases").EnumerateArray()) by3[c.GetProperty("name").GetString()] = c;
            foreach (var c2 in v2.RootElement.GetProperty("cases").EnumerateArray())
            {
                string name = c2.GetProperty("name").GetString();
                var a = Place(c2, 50.0, map); var b = Place(by3[name], 50.0, map);
                Assert.AreEqual(a.Count, b.Count, name);
                for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], name + " #" + i);
            }
        }
    }
}
