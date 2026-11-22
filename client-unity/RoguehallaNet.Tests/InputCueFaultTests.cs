using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-INPUT-CUES-1 review note 1: a presentation cue subscriber that throws must not cost a frame or the walk
    /// prediction's order bookkeeping. The flush completes, then the subscriber's exception surfaces.
    /// </summary>
    public class InputCueFaultTests
    {
        sealed class QueuedTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public readonly List<ClientMsg.Input> Inputs = new List<ClientMsg.Input>();
            readonly Queue<byte[]> incoming = new Queue<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) { if (ClientMsg.Decode(frame) is ClientMsg.Input input) Inputs.Add(input); }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { while (incoming.Count != 0) Received(incoming.Dequeue()); }
            public void Open() { Opened(); }
            public void Enqueue(ServerMsg msg) { incoming.Enqueue(msg.Encode()); }
        }

        [Test]
        public void AThrowingCueSubscriberCostsNoFrameAndNoOrderBookkeeping()
        {
            var t = new QueuedTransport();
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

            int cues = 0;
            s.CueSent += c => { cues++; throw new InvalidOperationException("faulty presentation subscriber"); };

            // A Move, then a cue-able command, then enough commands to need a second frame (16 commands per frame).
            byte move = s.OrderMove(150, 100);
            s.Input.UseConsumable();
            for (int i = 0; i < Wire.MaxCommandsPerInput; i++) s.Input.Swap();
            Assert.IsTrue(s.Motion.OrderPending);

            Assert.Throws<InvalidOperationException>(() => s.FlushInput(), "the subscriber's fault still surfaces");
            Assert.AreEqual(2, t.Inputs.Count, "both frames of the flush were sent");
            Assert.AreEqual(1 + Wire.MaxCommandsPerInput, cues, "every cue was still raised");

            // The server echoes the move in the frame that carried it: the prediction must recognise its own order.
            t.Enqueue(new ServerMsg.Snapshot
            {
                Tick = 2, Phase = 1, AckInput = t.Inputs[0].Seq,
                Me = new WireSelf { Id = 3, X = 100, Y = 100, Hp = 10, MaxHp = 10, Status = 1, MoveSeq = move }
            });
            s.Poll();
            Assert.IsFalse(s.Motion.OrderPending, "the move's sent-frame bookkeeping ran before the cue");
        }
    }
}
