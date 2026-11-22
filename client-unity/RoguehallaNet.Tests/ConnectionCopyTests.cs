using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-ALPHA-CLIENT-1: player-facing wording for rejects and terminal connection states.</summary>
    public class ConnectionCopyTests
    {
        const string Raw = "raw-reason-xyz";
        static readonly string[] Leaks = { Raw, "websocket", "1006", "attempts:", "Exception", "reconnect failed", "closed before welcome" };

        static void AssertPlayerCopy(string text, string what)
        {
            Assert.That(text, Is.Not.Null.And.Not.Empty, what);
            Assert.That(text.Trim(), Is.EqualTo(text), what + " has stray whitespace");
            foreach (var leak in Leaks) Assert.That(text, Does.Not.Contain(leak).IgnoreCase, what + " leaks '" + leak + "'");
            foreach (var name in Enum.GetNames(typeof(RejectCode))) Assert.That(text, Does.Not.Contain(name), what + " names the code " + name);
        }

        [Test]
        public void EveryRejectCodeAndAnUnknownOneMapsToPlayerCopyWithoutTheRawReason()
        {
            foreach (RejectCode code in Enum.GetValues(typeof(RejectCode)))
            {
                AssertPlayerCopy(ConnectionCopy.ForReject(code, Raw), code.ToString());
                AssertPlayerCopy(ConnectionCopy.ForReject(code, null), code + "/null reason");
                AssertPlayerCopy(ConnectionCopy.ForReject(code, ""), code + "/empty reason");
            }
            // The wire is append only: a code this build does not know still gets copy.
            AssertPlayerCopy(ConnectionCopy.ForReject((RejectCode)999, Raw), "unknown code");
        }

        [Test]
        public void TheNamedRejectsReadAsTheOwnerWordedThem()
        {
            Assert.AreEqual("The server is restarting. Try again in a minute.", ConnectionCopy.ForReject(RejectCode.ServerDraining, Raw));
            Assert.AreEqual("That match already started or is full. Join the next lobby.", ConnectionCopy.ForReject(RejectCode.MatchFull, Raw));
            Assert.AreEqual("That callsign is already in a match.", ConnectionCopy.ForReject(RejectCode.ProfileInUse, Raw));
            Assert.AreEqual("The game was updated. Reload the page.", ConnectionCopy.ForReject(RejectCode.ProtocolMismatch, Raw));
            Assert.AreEqual("The game was updated. Reload the page.", ConnectionCopy.ForReject(RejectCode.ContentMismatch, Raw));
            Assert.AreEqual("Too many attempts. Wait a few seconds and try again.", ConnectionCopy.ForReject(RejectCode.RateLimited, Raw));
        }

        [Test]
        public void EveryFailureKindMapsToPlayerCopyAndTheNamedOnesAreExact()
        {
            foreach (FailureKind kind in Enum.GetValues(typeof(FailureKind))) AssertPlayerCopy(ConnectionCopy.ForFailure(kind), kind.ToString());
            Assert.AreEqual("Lost connection to the server. Your run couldn't be recovered.", ConnectionCopy.ForFailure(FailureKind.LostConnection));
            // Kept: the existing Unity test pins that a Failed launch starts with this phrase.
            Assert.That(ConnectionCopy.ForFailure(FailureKind.ConnectTimeout), Does.StartWith("Couldn't reach the game server"));
            Assert.That(ConnectionCopy.ForFailure(FailureKind.Other), Does.StartWith("Couldn't reach the game server"));
            Assert.That(ConnectionCopy.ForFailure(FailureKind.None), Does.StartWith("Couldn't reach the game server"));
        }

        [Test]
        public void EndedIsNotANetworkErrorAndStatesMapToTheRightCopy()
        {
            string ended = ConnectionCopy.ForState(SessionState.Ended, null, null, FailureKind.None);
            AssertPlayerCopy(ended, "Ended");
            foreach (var bad in new[] { "reach", "connection", "network", "error", "failed", "couldn't" })
                Assert.That(ended, Does.Not.Contain(bad).IgnoreCase, "Ended reads as a network error: " + bad);

            Assert.AreEqual(ConnectionCopy.ForReject(RejectCode.MatchFull, Raw), ConnectionCopy.ForState(SessionState.Rejected, RejectCode.MatchFull, Raw, FailureKind.None));
            // A Rejected state with no code (never seen from a real server) still gives copy and never the raw reason.
            AssertPlayerCopy(ConnectionCopy.ForState(SessionState.Rejected, null, Raw, FailureKind.None), "Rejected/no code");
            Assert.AreEqual(ConnectionCopy.ForFailure(FailureKind.LostConnection), ConnectionCopy.ForState(SessionState.Failed, null, null, FailureKind.LostConnection));
            foreach (var live in new[] { SessionState.Idle, SessionState.Connecting, SessionState.HelloSent, SessionState.InMatch, SessionState.Reconnecting })
                Assert.AreEqual("", ConnectionCopy.ForState(live, null, null, FailureKind.None), live.ToString());
        }
    }
}
