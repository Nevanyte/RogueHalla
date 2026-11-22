using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-INPUT-CUES-1: Session.CueSent is raised in FlushInput, once per sent Cast, Swap, Pickup or UseConsumable.</summary>
    public class InputCueTests
    {
        sealed class QueuedTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public int InputFrames;
            readonly Queue<byte[]> incoming = new Queue<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) { if (ClientMsg.Decode(frame) is ClientMsg.Input) InputFrames++; }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { while (incoming.Count != 0) Received(incoming.Dequeue()); }
            public void Open() { Opened(); }
            public void Enqueue(ServerMsg msg) { incoming.Enqueue(msg.Encode()); }
        }

        static Session InMatch(QueuedTransport t)
        {
            var s = new Session(t, new SessionOptions { Url = "ws://test/ws", Profile = "p", Now = () => 1000 });
            s.Connect(); t.Open();
            t.Enqueue(new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], PlayerId = 3, Session = new byte[16],
                TickRate = 20, Tick = 1, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024])
            });
            t.Enqueue(new ServerMsg.Snapshot
            {
                Tick = 1, Phase = 1, Me = new WireSelf { Id = 3, X = 100, Y = 100, Hp = 10, MaxHp = 10, Status = 1 }
            });
            s.Poll();
            Assert.AreEqual(SessionState.InMatch, s.State);
            return s;
        }

        [Test]
        public void EachCueableIntentRaisesOneCueOnTheFrameItLeaves()
        {
            var t = new QueuedTransport();
            var s = InMatch(t);
            var cues = new List<WireCommand>();
            var framesAtCue = new List<int>();
            s.CueSent += c => { cues.Add(c); framesAtCue.Add(t.InputFrames); };

            Assert.AreNotEqual(0, s.CollectSpellInput(0, false, true, 10, 20));
            Assert.AreNotEqual(0, s.CollectSpellInput(1, false, true, 11, 21));
            Assert.AreNotEqual(0, s.CollectSpellInput(2, false, true, 12, 22));
            s.Input.Swap();
            s.Input.Pickup(new PickupHandle(7));
            s.Input.UseConsumable();
            s.OrderMove(50, 60);
            s.Input.Aim(0.5f);
            s.Input.Attack(0.5f, 50);
            Assert.AreEqual(0, cues.Count, "queuing raises no cue");

            s.FlushInput();

            Assert.AreEqual(1, t.InputFrames, "one frame left");
            Assert.AreEqual(6, cues.Count, "one cue per cueable command, none for Move, Aim or Attack");
            CommandKind[] kinds = { CommandKind.Cast, CommandKind.Cast, CommandKind.Cast, CommandKind.Swap, CommandKind.Pickup, CommandKind.UseConsumable };
            for (int k = 0; k < 6; k++) Assert.AreEqual(kinds[k], cues[k].Kind, "cue " + k);
            for (int k = 0; k < 3; k++) Assert.AreEqual((byte?)k, cues[k].Slot, "cast slot " + k);
            Assert.AreEqual(7u, cues[4].PickupRef.Value);
            foreach (int n in framesAtCue) Assert.AreEqual(1, n, "raised after the frame is sent");

            s.FlushInput();
            Assert.AreEqual(6, cues.Count, "nothing is cued twice");
        }

        [Test]
        public void NoCueForWhatNeverLeaves()
        {
            var t = new QueuedTransport();
            var s = InMatch(t);
            int cues = 0;
            s.CueSent += c => cues++;
            s.Input.Cast(0, float.NaN, 2);
            s.Input.Pickup(default(PickupHandle));
            s.FlushInput();
            Assert.AreEqual(0, cues, "dropped commands are never cued");
            Assert.AreEqual(0, t.InputFrames);

            var idleTransport = new QueuedTransport();
            var idle = new Session(idleTransport, new SessionOptions { Url = "ws://test/ws", Profile = "p", Now = () => 1000 });
            idle.CueSent += c => cues++;
            idle.Input.Swap();
            idle.FlushInput();
            Assert.AreNotEqual(SessionState.InMatch, idle.State);
            Assert.AreEqual(0, cues, "a frame that was not sent is never cued");
            Assert.AreEqual(0, idleTransport.InputFrames);
        }
    }
}
