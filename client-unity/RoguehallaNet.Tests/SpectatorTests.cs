using NUnit.Framework;
using Roguehalla.Net;

namespace Roguehalla.Net.Tests
{
    /// <summary>The client's mirror of the server's spectate rule (rh-sim spectate::tests): whom a spectator follows.</summary>
    public class SpectatorTests
    {
        [Test]
        public void The_requested_player_is_watched_while_standing()
        {
            Assert.AreEqual(5u, Spectator.Pick(5, new uint[] { 2, 5, 9 }));
        }

        [Test]
        public void A_fallen_request_moves_to_the_next_standing_player_and_wraps()
        {
            Assert.AreEqual(9u, Spectator.Pick(6, new uint[] { 2, 5, 9 }), "next after 6");
            Assert.AreEqual(2u, Spectator.Pick(10, new uint[] { 2, 5, 9 }), "wraps to the lowest");
            Assert.AreEqual(0u, Spectator.Pick(3, new uint[0]), "nobody delivered yet");
        }
    }
}
