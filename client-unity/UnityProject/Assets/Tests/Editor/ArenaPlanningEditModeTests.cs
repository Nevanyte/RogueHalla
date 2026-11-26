using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    public sealed class ArenaPlanningEditModeTests
    {
        GameObject _canvas, _owner;
        TacticalMapController _map;
        Session _session;
        readonly PlanningTransport _transport = new PlanningTransport();
        uint _tick;

        [TearDown]
        public void TearDown()
        {
            if (_map != null) _map.Bind(null);
            _session?.Leave();
            UnityEngine.Object.DestroyImmediate(_owner);
            UnityEngine.Object.DestroyImmediate(_canvas);
        }

        [TestCase(960, 600)]
        [TestCase(1280, 800)]
        public void DecisionsFitTheCardAndOnlyShowOfferedChoices(int width, int height)
        {
            Open(width, height);
            var plan = Deal();
            plan.Spawn0 = null;
            plan.Option0 = null;
            plan.Boons = new[] { "Bonus starting health", "Bonus starting attack speed" };
            Deliver(plan);

            Assert.IsFalse(Part("PlanningDecisions/Spawn0").gameObject.activeSelf);
            Assert.IsFalse(Part("MapFrame/PublicMap/SpawnMarker0").gameObject.activeSelf);
            Assert.IsTrue(ButtonAt("PlanningDecisions/Spawn1").interactable);
            StringAssert.StartsWith("2 ·", LabelAt("PlanningDecisions/Spawn1/Label").text);
            Assert.IsTrue(ButtonAt("PlanningDecisions/Boon0").interactable);
            Assert.IsTrue(ButtonAt("PlanningDecisions/Boon1").interactable);
            Assert.IsTrue(ButtonAt("PlanningDecisions/Ready").interactable);
            StringAssert.Contains("server places you", LabelAt("PlanningDecisions/ReadyDetail").text);
            RectTransform card = Part("PlanningDecisions");
            foreach (string path in new[] { "MapStatus", "PlanningDecisions/Spawn1", "PlanningDecisions/Boon1", "PlanningDecisions/Ready", "PlanningDecisions/ReadyDetail", "ClearRoute" })
            {
                var corners = new Vector3[4];
                Part(path).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector3 local = card.InverseTransformPoint(corner);
                    Assert.That(local.x, Is.InRange(card.rect.xMin - .1f, card.rect.xMax + .1f), path);
                    Assert.That(local.y, Is.InRange(card.rect.yMin - .1f, card.rect.yMax + .1f), path);
                }
            }
            Assert.That(LabelAt("MapStatus").fontSize * HudLayout.Viewport(width, height).Scale, Is.GreaterThanOrEqualTo(12f));
        }

        [Test]
        public void DefaultReadyWaitsForConfirmationAndExplainsTheArenaStart()
        {
            Open(960, 600);
            var plan = Deal();
            Deliver(plan);
            Button ready = ButtonAt("PlanningDecisions/Ready");
            ready.onClick.Invoke();
            _session.FlushInput();
            _map.Tick();

            Assert.That(_transport.Inputs, Has.Count.EqualTo(1), "Ready uses the regular input sender even without picks");
            Assert.IsFalse(_session.Collections.Plan.Ready);
            StringAssert.Contains("PENDING", LabelAt("PlanningDecisions/Ready/Label").text);
            StringAssert.Contains("waiting for server confirmation", LabelAt("MapStatus").text);
            Assert.IsFalse(ButtonAt("MapFrame/PublicMap/SpawnMarker0").interactable);

            plan.Ready = true;
            Deliver(plan);
            StringAssert.Contains("CONFIRMED", LabelAt("PlanningDecisions/Ready/Label").text);
            StringAssert.Contains("The arena starts", LabelAt("PlanningDecisions/ReadyDetail").text);
            Assert.IsFalse(ready.interactable);
            Assert.IsNull(_session.Collections.Plan.Pick, "the server will assign the spawn at the start");
        }

        [Test]
        public void MapSelectionDoesNotBecomeConfirmedDuringReconnect()
        {
            Open(960, 600);
            Deliver(Deal());
            ButtonAt("MapFrame/PublicMap/SpawnMarker0").onClick.Invoke();
            _map.Tick();
            StringAssert.Contains("Pending", LabelAt("PlanningDecisions/Spawn0/Label").text);
            StringAssert.DoesNotContain("Selected", LabelAt("PlanningDecisions/Spawn0/Label").text);
            StringAssert.Contains("ETA after spawn assignment", LabelAt("RouteSummary").text);

            _transport.Drop();
            _map.Tick();
            StringAssert.Contains("RECONNECTING", LabelAt("PlanningDecisions/Ready/Label").text);
            StringAssert.Contains("pending choices were cleared", LabelAt("MapStatus").text);
            StringAssert.DoesNotContain("Selected", LabelAt("PlanningDecisions/Spawn0/Label").text);
            Assert.IsFalse(ButtonAt("MapFrame/PublicMap/SpawnMarker0").interactable);
        }

        static WirePlan Deal() => new WirePlan
        {
            EndsAt = 45000, Spawn0 = 0, Spawn1 = 1,
            Option0 = new WirePlanOption { Index = 0, X = 16, Y = 16, Zone = 1 },
            Option1 = new WirePlanOption { Index = 1, X = 48, Y = 48, Zone = 2 }
        };

        void Open(int width, int height)
        {
            _canvas = new GameObject("Planning canvas", typeof(RectTransform), typeof(Canvas));
            _owner = new GameObject("Planning controller");
            var stage = HudUi.At(_canvas.transform, "Stage", 0, 0, 0, 0);
            var viewport = HudLayout.Viewport(width, height);
            stage.sizeDelta = new Vector2(viewport.UiWidth, viewport.UiHeight);
            stage.localScale = new Vector3(viewport.Scale, viewport.Scale, 1f);
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/planning", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
            _transport.Deliver(new ServerMsg.Snapshot { Tick = ++_tick, Phase = (byte)MatchPhase.Planning });
            var client = _owner.AddComponent<MatchClient>();
            client.enabled = false;
            client.Url = "ws://localhost/planning";
            client.Profile = "fixture";
            _map = _owner.AddComponent<TacticalMapController>();
            _map.Initialize(client, _canvas.transform, hudStage: stage);
            _map.Bind(_session);
        }

        void Deliver(WirePlan plan)
        {
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new PlanState { Value = plan } } });
            _map.Tick();
            Canvas.ForceUpdateCanvases();
        }

        RectTransform Part(string path) => (RectTransform)_canvas.transform.Find("TacticalMap/" + path);
        TMP_Text LabelAt(string path) => Part(path).GetComponent<TMP_Text>();
        Button ButtonAt(string path) => Part(path).GetComponent<Button>();

        sealed class PlanningTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public readonly List<ClientMsg.Input> Inputs = new List<ClientMsg.Input>();
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
            public void Drop() => Closed?.Invoke("fixture disconnect");
            public void Connect(string url) { }
            public void Send(byte[] frame) { if (ClientMsg.Decode(frame) is ClientMsg.Input input) Inputs.Add(input); }
            public void Close() { }
            public void Poll() { }
        }
    }
}
