using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>Historical protocol-18 fixtures remain covered while the active client speaks protocol 23.</summary>
    public class Protocol18StampTests
    {
        [Test] public void CodecSpeaksProtocol23() { Assert.AreEqual(23, Wire.ProtocolVersion); }

        [Test]
        public void V18FixtureExistsAndItsHelloSays18()
        {
            string path = Path.Combine(Util.RepoRoot(), "protocol", "fixtures", "v18.json");
            Assert.IsTrue(File.Exists(path), "protocol/fixtures/v18.json is missing (cargo run -p rh-protocol --bin protocol-fixtures)");
            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.AreEqual(18, root.GetProperty("protocol").GetInt32());
            string helloHex = null, welcomeHex = null;
            foreach (var v in root.GetProperty("client").EnumerateArray())
                if (v.GetProperty("name").GetString() == "hello") helloHex = v.GetProperty("hex").GetString();
            foreach (var v in root.GetProperty("server").EnumerateArray())
                if (v.GetProperty("name").GetString() == "welcome") welcomeHex = v.GetProperty("hex").GetString();
            Assert.IsNotNull(helloHex, "v18.json has a hello vector"); Assert.IsNotNull(welcomeHex, "v18.json has a welcome vector");
            // These are preserved as historical bytes. The active decoder correctly refuses obsolete versions.
            Assert.AreEqual(18, (ushort)(Util.FromHex(helloHex)[1] | Util.FromHex(helloHex)[2] << 8));
            Assert.AreEqual(18, (ushort)(Util.FromHex(welcomeHex)[1] | Util.FromHex(welcomeHex)[2] << 8));
        }

        static Session NewSession(FakeTransport t)
        {
            return new Session(t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Ticket = new byte[] { 1 }, Now = () => 1000, ReconnectBaseMs = 500, MaxReconnectAttempts = 3 });
        }

        static ServerMsg.Welcome Welcome(ushort protocol)
        {
            return new ServerMsg.Welcome { Protocol = protocol, MatchId = new byte[16], PlayerId = 3, Session = new byte[16], TickRate = 20, Tick = 1, ServerMs = 50, Map = new MapBlob(4, 4, new byte[16]) };
        }

        [Test]
        public void AProtocol22WelcomeIsRefusedAndA23WelcomeAccepted()
        {
            var old = new FakeTransport(); var s22 = NewSession(old);
            s22.Connect(); old.Open();
            Assert.AreEqual(23, old.LastSent<ClientMsg.Hello>().Protocol, "the Hello says 23");
            old.Deliver(Welcome(22));
            Assert.AreEqual(SessionState.Failed, s22.State, "a protocol-22 Welcome is refused");
            StringAssert.Contains("server protocol 22", s22.Error);

            var cur = new FakeTransport(); var s23 = NewSession(cur);
            s23.Connect(); cur.Open();
            cur.Deliver(Welcome(23));
            Assert.AreEqual(SessionState.InMatch, s23.State, "a protocol-23 Welcome starts the match");
        }
    }
}
