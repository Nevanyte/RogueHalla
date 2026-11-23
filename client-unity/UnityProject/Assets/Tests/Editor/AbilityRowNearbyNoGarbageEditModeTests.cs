using System;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-ABILITY-ROW-1-fix: one call of MatchHud.RefreshQuestRewardHud allocates nothing in the states the first allocation tests of the
    /// ability row never built: nearby quest actions (the caption is built when the actions are bound), a pending reward choice with the overlay open, sent
    /// and awaited, and deferred by LATER (no boxed nullable compare), a nearby action beside the pickup pill, a cooling spell, and a board
    /// row alone. A whole MatchHud on a real MatchClient over a fake transport, as AbilityRowEditModeTests builds it. Every snapshot carries
    /// server time 1000, so no spell key moves between the measured calls. Nothing here reads a clock.
    /// </summary>
    public sealed class AbilityRowNearbyNoGarbageEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string LauncherPath = "PlayerHUD/QuestRewards/QuestActionLauncher";

        sealed class Transport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() => Closed?.Invoke("closed");
            public void Poll() { }
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
        }

        GameObject _fullRoot, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        UiTheme _savedTheme;

        void BuildFullRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Nearby garbage events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Nearby garbage fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://nearby/ws", Profile = "nearby", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(1, 50);
        }

        void Snap(uint tick, double serverMs, byte phase = (byte)MatchPhase.Active) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        void Items(uint tick, params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = tick, Items = items });

        /// <summary>One frame of the HUD's late update (the quest refresh runs in it, and once more through the player-flow tick).</summary>
        void Frame() { Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        void LayoutFull(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        static QuestsState Quests(WireQuestSpell[] spells = null, WireQuestAction[] actions = null, WireQuestDrop[] drops = null)
        {
            var view = new WireQuestView();
            if (spells != null) view.Spells = spells;
            if (actions != null) view.Actions = actions;
            if (drops != null) view.Drops = drops;
            return new QuestsState { Available = true, View = view };
        }

        static QuestsState NearbyAction() => Quests(actions: new[] { new WireQuestAction { Quest = new QuestHandle(71), Action = 9, Target = 37,
            Label = "Pick up WallBuster", Points = new[] { 64.0, 64.0 } } },
            drops: new[] { new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = 64, Y = 64 } });

        static PropsState PickupInReach(bool inReach) => new PropsState
        {
            Rows = new[] { new WireProp { Handle = new PickupHandle(5), X = 300, Y = 300, Kind = PropKind.Ground, GroundWeapon = (byte)WeaponId.Axe } },
            Focus = inReach ? new WireFocus { Handle = new PickupHandle(5) } : (WireFocus?)null,
        };

        static QuestsState QuestState(QuestHandle handle) => new QuestsState { Available = true, View = new WireQuestView {
            Rows = new[] { new WireQuestRow { Handle = handle, Def = "fixture", GiverX = 64, GiverY = 80,
                State = QuestRowState.Preview, ExpiresAt = 10000, CanAccept = true, Name = "same", GiverLabel = "same",
                Brief = "native brief", Objective = "native objective", Reward = "native reward" } },
            Objectives = new[] { new WireQuestObjective { Quest = handle, X = 200, Y = 220 } } } };

        /// <summary>The state of PlayerFlowsEditModeTests.FullQuestSlotsPresentServerChoiceAgainAfterReconnectAndSendTheChosenTuple: a pending
        /// choice with three buttons and two spells.</summary>
        static QuestsState PendingChoice()
        {
            var handle = new QuestHandle(71);
            var state = QuestState(handle);
            state.View.Rows[0].State = QuestRowState.Open;
            state.View.Rows[0].Def = "barrow_run";
            state.View.Rows[0].OwnerAccepted = true;
            state.View.Rows[0].OwnerReady = true;
            state.View.Rows[0].CanAccept = false;
            state.View.Rows[0].Reward = "WallBuster";
            state.View.Spells = new[] { new WireQuestSpell { Slot = 0, Reward = 2, Charges = 2, ReadyAt = 2500 },
                new WireQuestSpell { Slot = 1, Reward = 4, Charges = 1, ReadyAt = 0 } };
            state.View.Pending = new[] { handle };
            state.View.Actions = new[] {
                new WireQuestAction { Quest = handle, Action = 8, Target = 0, Label = "Replace Shift spell" },
                new WireQuestAction { Quest = handle, Action = 8, Target = 1, Label = "Replace Q spell" },
                new WireQuestAction { Quest = handle, Action = 8, Target = 2, Label = "Leave new spell on the ground" },
            };
            return state;
        }

        [TearDown]
        public void TearDown()
        {
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------ helpers

        RectTransform FullStage => (RectTransform)GetF(_hud, "_stage");
        RectTransform FullBar => Child(FullStage, "PlayerHUD/CombatActions");
        RectTransform Tile(string name) => Child(FullBar, name);
        RectTransform Launcher => Child(FullStage, LauncherPath);
        string LauncherCaption() => Launcher.GetComponentInChildren<TMP_Text>(true).text;
        Transform Overlay => HudTestPaths.Find(_hud.transform, "MatchUI/QuestRewardChoiceOverlay");

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

        static string Text(Transform tile, string box) => Child(tile, box + "/Label").GetComponent<TMP_Text>().text;

        static object GetF(object o, string name)
        {
            var f = o.GetType().GetField(name, All);
            Assert.IsNotNull(f, "missing field " + name);
            return f.GetValue(o);
        }

        static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, All);
            Assert.IsNotNull(m, "missing method " + name);
            return m.Invoke(o, args.Length == 0 ? null : args);
        }

        Action Refresh() => (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));

        // ------------------------------------------------------------------ 1. nearby actions

        [Test]
        public void ANearbyActionAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, NearbyAction());
            Frame();
            Frame();
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: the launcher shows");
            Assert.That(Launcher.GetComponent<Button>().interactable, Is.True, "set-up: and can be pressed");
            Assert.That((int)GetF(_hud, "_nearbyQuestCount"), Is.EqualTo(1), "set-up: one nearby action");
            Assert.That(LauncherCaption(), Is.EqualTo("Pick up WallBuster"), "set-up: the caption");
            Assert.That(_session.ServerTimeMs, Is.EqualTo(1000d), "set-up: the server time is the delivered one");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with a nearby quest action");
        }

        // ------------------------------------------------------------------ 2. the caption follows the count

        [Test]
        public void TheNearbyCaptionFollowsTheCount()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, NearbyAction());
            Frame();
            Assert.That((int)GetF(_hud, "_nearbyQuestCount"), Is.EqualTo(1));
            Assert.That(LauncherCaption(), Is.EqualTo("Pick up WallBuster"));

            Snap(11, 1000);
            Items(11, Quests(
                actions: new[]
                {
                    new WireQuestAction { Quest = new QuestHandle(71), Action = 9, Target = 37, Label = "Pick up WallBuster", Points = new[] { 64.0, 64.0 } },
                    new WireQuestAction { Quest = new QuestHandle(72), Action = 9, Target = 38, Label = "Pick up Hook", Points = new[] { 64.0, 64.0 } },
                },
                drops: new[]
                {
                    new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = 64, Y = 64 },
                    new WireQuestDrop { Index = 38, Reward = 2, Charges = 1, X = 64, Y = 64 },
                }));
            Frame();
            Assert.That((int)GetF(_hud, "_nearbyQuestCount"), Is.EqualTo(2));
            Assert.That(LauncherCaption(), Is.EqualTo("Nearby quest actions · 2"));

            Snap(12, 1000);
            Items(12, NearbyAction());
            Frame();
            Assert.That((int)GetF(_hud, "_nearbyQuestCount"), Is.EqualTo(1));
            Assert.That(LauncherCaption(), Is.EqualTo("Pick up WallBuster"));
        }

        // ------------------------------------------------------------------ 3 to 5. a pending choice

        [Test]
        public void APendingChoiceWithTheOverlayOpenAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, PendingChoice());
            Frame();
            Frame();
            Assert.That(Overlay.gameObject.activeSelf, Is.True, "set-up: the pending choice opens its overlay");
            Assert.That(_hud.BlocksKeyboardInput, Is.True, "set-up: the overlay blocks the keyboard");
            Assert.That(GetF(_hud, "_questChoice"), Is.Not.Null, "set-up: a choice is pending");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with the reward overlay open");
        }

        [Test]
        public void AChoiceSentAndAwaitedAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, PendingChoice());
            Frame();
            Frame();
            Child(Overlay, "QuestRewardChoice/RewardChoice1").GetComponent<Button>().onClick.Invoke();
            Frame();
            Assert.That(Overlay.gameObject.activeSelf, Is.True, "set-up: the overlay is still open");
            Assert.That(GetF(_hud, "_questChoiceSent"), Is.Not.Null, "set-up: the choice was sent");
            Assert.That(((TMP_Text)GetF(_hud, "_questChoiceStatus")).text, Is.EqualTo("Waiting for server…"), "set-up: awaiting the server");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames while the sent choice is awaited");
        }

        [Test]
        public void ADeferredChoiceAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, PendingChoice());
            Frame();
            Frame();
            Child(Overlay, "QuestRewardChoice/ChooseLater").GetComponent<Button>().onClick.Invoke();
            EventSystem.current.SetSelectedGameObject(null);
            Frame();
            Assert.That(Overlay.gameObject.activeSelf, Is.False, "set-up: LATER closed the overlay");
            Assert.That(_hud.BlocksKeyboardInput, Is.False, "set-up: nothing blocks the keyboard");
            Assert.That(GetF(_hud, "_questChoiceDeferred"), Is.Not.Null, "set-up: the choice is deferred");
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: the launcher shows");
            Assert.That(Launcher.GetComponent<Button>().interactable, Is.True, "set-up: and can be pressed");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with the choice deferred");
        }

        // ------------------------------------------------------------------ 6. beside the pickup pill

        [Test]
        public void ANearbyActionBesideThePickupPillAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, NearbyAction());
            Frame();
            Frame();
            Snap(11, 1000);
            Items(11, PickupInReach(true), NearbyAction());   // Props (key 2) before Quests (key 10)
            Frame();
            Frame();
            Assert.That(FullBar.Find("Pickup").gameObject.activeInHierarchy, Is.True, "set-up: the pickup pill shows");
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: the launcher shows");
            Assert.That((bool)GetF(_hud, "_promptPair"), Is.True, "set-up: they share the lane");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with a nearby action beside the pickup pill");
        }

        // ------------------------------------------------------------------ 7. a cooling spell

        [Test]
        public void ACoolingQuestSpellAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Snap(10, 1000);
            Items(10, Quests(new[]
            {
                new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 6000 },
                new WireQuestSpell { Slot = 1, Reward = 4, Charges = 1, ReadyAt = 0 },
            }));
            Frame();
            Frame();
            Assert.That(Text(Tile("QuestSpell1"), "Seconds"), Is.EqualTo("5"), "set-up: the first spell cools");
            Assert.That(Text(Tile("QuestSpell2"), "Name"), Is.EqualTo("Tornado"), "set-up: the second is ready");
            Assert.That(((TMP_Text)GetF(_hud, "_questChoiceSpells")).text, Does.Contain("2 charges · 5s"), "set-up: the equipped line");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with one spell cooling and one ready");
        }

        // ------------------------------------------------------------------ 8. a board quest alone

        [Test]
        public void ABoardQuestAloneAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            var state = QuestState(new QuestHandle(71));
            state.View.Rows[0].Def = "letters";
            state.View.Rows[0].State = QuestRowState.Open;
            state.View.Rows[0].OwnerAccepted = true;
            state.View.Rows[0].CanAccept = false;
            Snap(10, 1000);
            Items(10, state);
            Frame();
            Frame();
            Assert.That(Child(FullStage, "PlayerHUD/QuestRewards").gameObject.activeSelf, Is.True, "set-up: the box shows for the board row");
            Assert.That(Launcher.gameObject.activeSelf, Is.False, "set-up: the launcher is hidden");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with a board quest row alone");
        }
    }
}
