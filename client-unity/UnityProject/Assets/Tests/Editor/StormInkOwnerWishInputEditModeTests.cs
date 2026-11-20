using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client.Tests
{
    /// <summary>Exercise the production Wish frame in a room that offers its Frost players a placed wall.</summary>
    public sealed class StormInkOwnerWishInputEditModeTests
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase(FactionId.Fire)]
        [TestCase(FactionId.Sentenced)]
        [TestCase(FactionId.Plague)]
        [TestCase(FactionId.Holy)]
        [TestCase(FactionId.Frost)]
        public async Task OnlyTheFrostOwnerOpensThePlacedWallOnR(FactionId faction)
        {
            var type = typeof(TrainingConsoleIntegrationEditModeTests).GetNestedType("Fixture", BindingFlags.NonPublic);
            using (var fixture = (IDisposable)Activator.CreateInstance(type, true))
            {
                await (Task)type.GetMethod("JoinReady", Members).Invoke(fixture, new object[] { MatchLaunchMode.Training });
                type.GetMethod("Welcome", Members).Invoke(fixture, null);
                type.GetMethod("Snapshot", Members).Invoke(fixture, new object[] { true, 1u, 50.0 });
                var client = (MatchClient)type.GetField("Client", Members).GetValue(fixture);
                var session = client.Session;
                session.Me.Faction = faction;
                double[] terms = { 1, 450, 64, 224, 4000, 24, 8 };
                var block = new WireHudTerm[terms.Length];
                for (int i = 0; i < terms.Length; i++)
                    block[i] = new WireHudTerm { Code = (byte)((int)HudTerm.FrostWish + i), Value = terms[i] };
                session.Welcome.Hud.Terms = block;
                var frame = (WishAimFrame)typeof(MatchClient).GetMethod("MakeWishFrame", Members).Invoke(client,
                    new object[] { 320f, 320f, false, true, 440f, 318f, null });

                byte sequence = client.WishAim.Key(session, frame);
                bool frost = faction == FactionId.Frost;
                Assert.That(client.WishAim.Aiming, Is.EqualTo(frost), faction + " R input");
                Assert.That(sequence != 0, Is.EqualTo(!frost), "instant Wishes submit on R");
                Assert.That(frame.Numbers.HasValue, Is.True, "observers still receive the room wall dimensions");
            }
        }
    }
}
