using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-QUEST-OFFER-1-fix oracle: the quest journal's ACCEPT and the offer card's Enter share one latch owned by the HUD.
    /// A hold lasts 2.5 s of frame ticks, ends when the server's answer changes the row (not when a view arrives with the row
    /// unchanged), and a dropped command is offered again afterwards. For 2.5 s after an accept press the journal's Abandon of
    /// that quest takes no press. One rig: the fixture of <c>QuestOfferEditModeTests</c> (a real MatchClient and MatchHud on a fake
    /// transport, a real keyboard): the player is 3 at (320, 320), alive, in an Active match, server time 1000. Every test runs in
    /// the order of a match: rows are delivered and refreshed while the journal is hidden, then J opens it. Time moves only
    /// through <c>MatchHud.TickOffer(dt)</c> (EditMode time does not advance).
    /// </summary>
    public sealed class QuestAcceptLatchEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float Eps = .6f;
        const string CardPath = "MatchUI/HudStage/PlayerHUD/QuestOffer";
        const string LauncherPath = "MatchUI/HudStage/PlayerHUD/QuestRewards/QuestActionLauncher";
        static readonly Vector2[] Windows = { new Vector2(1280, 800), new Vector2(1920, 1080), new Vector2(2560, 1440) };
        static readonly QuestHandle Near = new QuestHandle(71), Far = new QuestHandle(72), Pending = new QuestHandle(90);

        sealed class FakeTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            readonly Queue<byte[]> _incoming = new Queue<byte[]>();
            public readonly List<byte[]> Sent = new List<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) { Sent.Add(frame); }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { while (_incoming.Count != 0) Received?.Invoke(_incoming.Dequeue()); }
            public void Open() { Opened?.Invoke(); }
            public void Deliver(ServerMsg message) { _incoming.Enqueue(message.Encode()); }
            public List<WireCommand> TakeCommands()
            {
                var commands = new List<WireCommand>();
                for (int i = 0; i < Sent.Count; i++)
                    if (ClientMsg.Decode(Sent[i]) is ClientMsg.Input input)
                        commands.AddRange(input.Commands);
                Sent.Clear();
                return commands;
            }
        }

        GameObject _root, _ownedEvents;
        EventSystem _previousEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        FakeTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Keyboard _previousKeyboard;
        Mouse _previousMouse;
        InputSettings _originalInputSettings, _fixtureInputSettings;
        HideFlags _originalInputSettingsHideFlags;
        bool _oldToggleMode, _cleanupFinished;
        bool? _priorReduced;
        uint _tick = 2;

        // ------------------------------------------------------------------ the rig (UiInputRoutingEditModeTests, copied)

        [SetUp]
        public void SetUp()
        {
            _cleanupFinished = false;
            MatchHud.OfferFrameDeltaOverride = 0f;
            _priorReduced = UiMotion.ReducedOverride; UiMotion.ReducedOverride = true;
            _previousEvents = EventSystem.current;
            _originalInputSettings = InputSystem.settings;
            _previousKeyboard = Keyboard.current;
            _previousMouse = Mouse.current;
            try
            {
                Assert.That(_originalInputSettings != null, Is.True, "the manager must start with live InputSettings");
                _originalInputSettingsHideFlags = _originalInputSettings.hideFlags;
                SetUpFixture();
            }
            catch (Exception setupError)
            {
                try { TearDown(); }
                catch (Exception cleanupError) { throw new AggregateException(setupError, cleanupError); }
                throw;
            }
        }

        void SetUpFixture()
        {
            _ownedEvents = new GameObject("Quest accept latch test EventSystem", typeof(UiInputRoutingEventSystem));
            var ownedSystem = _ownedEvents.GetComponent<UiInputRoutingEventSystem>();
            ownedSystem.enabled = false;
            ownedSystem.enabled = true;
            EventSystem.current = ownedSystem;
            Assert.That(EventSystem.current, Is.SameAs(ownedSystem), "test EventSystem must be registered before UI setup");
            _fixtureInputSettings = ScriptableObject.Instantiate(_originalInputSettings);
            _fixtureInputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            _fixtureInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _fixtureInputSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            if (_originalInputSettingsHideFlags == HideFlags.HideAndDontSave)
                _originalInputSettings.hideFlags = HideFlags.DontSave;
            InputSystem.settings = _fixtureInputSettings;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _keyboard.MakeCurrent();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();

            _root = new GameObject("Quest accept latch fixture");
            _client = _root.AddComponent<MatchClient>();
            _client.enabled = false;
            Invoke(_client, "InitializeLayoutReview", CreateSession());
            _hud = _client.Hud;
            _session = _client.Session;
            _oldToggleMode = _hud.TacticalMap.ToggleMode;
            _hud.TacticalMap.SetToggleMode(false);

            _session.Connect();
            _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 3,
                TickRate = 20, Tick = 1, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024])
            });
            _transport.Deliver(new ServerMsg.State
            {
                Tick = 1,
                Items = new StateItem[]
                {
                    new RewardChestsState { Rows = new[] { new WireRewardChest
                    {
                        Handle = new RewardChestHandle(73), X = 320, Y = 320, ExpiresAt = 15000, CanChoose = true,
                        Options = new[]
                        {
                            new WireRewardOption { Kind = 1, Id = 0 },
                            new WireRewardOption { Kind = 1, Id = 79 },
                            new WireRewardOption { Kind = 2, Id = 7 }
                        }
                    } } }
                }
            });
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = 1, ServerTimeOverrideMs = 1000, Phase = 1,
                Me = new WireSelf { Id = 3, X = 320, Y = 320, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 },
                Actors = new[] { new WireActor { Id = 4, X = 322, Y = 320, Faction = 2, HpPct = 100, Status = 1 } }
            });
            _session.Poll();
            Invoke(_hud, "LateUpdate");
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch));
            Assert.That(_session.Me, Is.Not.Null, "the rig has a local player");
            _transport.TakeCommands();
        }

        Session CreateSession()
        {
            _transport = new FakeTransport();
            return new Session(_transport, new SessionOptions { Url = "ws://quest-latch-test/ws", Profile = "latch-test", Now = () => 1000 });
        }

        [TearDown]
        public void TearDown()
        {
            if (_cleanupFinished) return;
            _cleanupFinished = true;
            try
            {
                UiMotion.ReducedOverride = _priorReduced;
                MatchHud.OfferFrameDeltaOverride = null;
                try
                {
                    if (_hud != null && _hud.TacticalMap != null) _hud.TacticalMap.SetToggleMode(_oldToggleMode);
                }
                finally
                {
                    if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
                }
            }
            finally
            {
                try
                {
                    if (_ownedEvents != null)
                    {
                        var ownedSystem = _ownedEvents.GetComponent<EventSystem>();
                        if (ownedSystem != null && EventSystem.current == ownedSystem) ownedSystem.SetSelectedGameObject(null);
                        _ownedEvents.SetActive(false);
                        UnityEngine.Object.DestroyImmediate(_ownedEvents);
                    }
                }
                finally
                {
                    if (_previousEvents != null && EventSystem.current != _previousEvents) EventSystem.current = _previousEvents;
                    try
                    {
                        if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
                        if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                    }
                    finally
                    {
                        if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent();
                        if (_previousMouse != null && _previousMouse.added) _previousMouse.MakeCurrent();
                        if (_originalInputSettings != null && ReferenceEquals(InputSystem.settings, _fixtureInputSettings))
                            InputSystem.settings = _originalInputSettings;
                        if (_originalInputSettings != null) _originalInputSettings.hideFlags = _originalInputSettingsHideFlags;
                        if (_fixtureInputSettings != null && !ReferenceEquals(InputSystem.settings, _fixtureInputSettings))
                            UnityEngine.Object.DestroyImmediate(_fixtureInputSettings);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ deliveries and keys

        /// <summary>One offer row: a giver near the player (330, 320) by default, a non-board definition, ready to be accepted.</summary>
        static WireQuestRow Offer(uint handle, double giverX = 330, bool canAccept = true, string name = "Clear the Barrow",
            string giver = "Old Marta", string objective = "Bring back three lanterns", string reward = "Hook", double expiresAt = 10000)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = "fixture", GiverX = giverX, GiverY = 320, State = QuestRowState.Preview, ExpiresAt = expiresAt,
                CanAccept = canAccept, Name = name, GiverLabel = giver, Brief = "brief", Objective = objective, Reward = reward,
            };
        }

        /// <summary>An accepted quest that is running: the tracker lists it.</summary>
        static WireQuestRow Running(uint handle)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = "hold_the_fort", GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = 61000,
                OwnerAccepted = true, OwnerProgress = 12, Name = "Hold the Fort", GiverLabel = "Captain", Brief = "brief",
                Objective = "Stay inside the ring", Reward = "Tornado",
            };
        }

        static QuestsState Quests(params WireQuestRow[] rows)
        {
            return new QuestsState { Available = true, View = new WireQuestView { Rows = rows } };
        }

        /// <summary>A pending reward choice (row 90, three actions) beside the given rows. Rows stay in ascending handle order.</summary>
        static QuestsState WithChoice(params WireQuestRow[] rows)
        {
            var choice = new WireQuestRow
            {
                Handle = Pending, Def = "barrow_run", GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = 0,
                OwnerAccepted = true, OwnerReady = true, Name = "Barrow Run", GiverLabel = "Warden", Brief = "brief",
                Objective = "Return", Reward = "WallBuster",
            };
            var all = new List<WireQuestRow>(rows) { choice };
            var state = Quests(all.ToArray());
            state.View.Pending = new[] { Pending };
            state.View.Actions = new[]
            {
                new WireQuestAction { Quest = Pending, Action = 8, Target = 0, Label = "Replace Shift spell" },
                new WireQuestAction { Quest = Pending, Action = 8, Target = 1, Label = "Replace Q spell" },
                new WireQuestAction { Quest = Pending, Action = 8, Target = 2, Label = "Leave new spell on the ground" },
            };
            return state;
        }

        static PropsState PickupInReach(bool inReach) => new PropsState
        {
            Rows = new[] { new WireProp { Handle = new PickupHandle(5), X = 300, Y = 300, Kind = PropKind.Ground, GroundWeapon = (byte)WeaponId.Axe } },
            Focus = inReach ? new WireFocus { Handle = new PickupHandle(5) } : (WireFocus?)null,
        };

        /// <summary>Delivers state items (ascending keys: Props 2, Quests 10, Readouts 16) and lets the session refresh the HUD.</summary>
        void DeliverItems(params StateItem[] items)
        {
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = items });
            _session.Poll();
        }

        void Deliver(params WireQuestRow[] rows) { DeliverItems(Quests(rows)); }

        /// <summary>One HUD frame as the client runs it: the late update, the player-flow tick, the terrain row and the readout strip.</summary>
        void Frame()
        {
            Invoke(_hud, "LateUpdate"); Invoke(_hud, "TickPlayerFlows"); Invoke(_hud, "TickTerrain"); Invoke(_hud, "RefreshReadouts");
        }

        void Tap(params Key[] keys) { Press(keys); ReadBothAndFlush(); ReleaseKeys(); }

        /// <summary>F with nothing to pick up, then the offer card must be open.</summary>
        void OpenCard()
        {
            _transport.TakeCommands();
            Tap(Key.F);
            Assert.That(_hud.OfferOpen, Is.True, "set-up: F opened the card");
            _transport.TakeCommands();
        }

        int Count(List<WireCommand> commands, CommandKind kind) { return commands.FindAll(c => c.Kind == kind).Count; }

        // ------------------------------------------------------------------ finding things

        Transform Card() { return HudTestPaths.Find(_hud.transform, CardPath); }

        Transform CardChild(string path)
        {
            var card = Card();
            Assert.That(card, Is.Not.Null, "the card 'QuestOffer' exists under PlayerHUD");
            if (card == null) return null;
            var t = card.Find(path);
            Assert.That(t, Is.Not.Null, "the card has '" + path + "'");
            return t;
        }

        string CardText(string path)
        {
            var t = CardChild(path);
            var label = t != null ? t.GetComponent<TMP_Text>() : null;
            Assert.That(label, Is.Not.Null, "'" + path + "' carries a TMP_Text");
            return label != null ? label.text : "";
        }

        /// <summary>A card label that is on screen: found, active and not empty.</summary>
        bool CardTextShown(string boxName)
        {
            var card = Card();
            if (card == null) return false;
            var box = card.Find(boxName);
            if (box == null || !box.gameObject.activeInHierarchy) return false;
            var label = box.Find("Label");
            return label != null && label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.GetComponent<TMP_Text>().text);
        }

        Button CardButton(string path)
        {
            var t = CardChild(path);
            var button = t != null ? t.GetComponent<Button>() : null;
            Assert.That(button, Is.Not.Null, "'" + path + "' is a Button");
            return button;
        }

        bool AcceptInteractable()
        {
            var button = CardButton("Footer/Accept");
            return button != null && button.interactable;
        }

        // ------------------------------------------------------------------ the journal, as a player opens it

        Transform JournalDetail(string path)
        {
            var t = _hud.QuestJournal.Root.Find("QuestDetail/" + path);
            Assert.That(t, Is.Not.Null, "the journal has QuestDetail/" + path);
            return t;
        }

        Button JournalButton(string name) { return JournalDetail(name).GetComponent<Button>(); }

        string JournalLabel(string name) { return JournalDetail(name + "/Label").GetComponent<TMP_Text>().text; }

        /// <summary>J opens the journal (the rows were delivered and refreshed while it was hidden, as in a match), then selects a quest.</summary>
        void OpenJournal(QuestHandle select)
        {
            Tap(Key.J);
            Assert.That(_hud.QuestJournal.IsVisible, Is.True, "set-up: J opened the journal");
            _hud.QuestJournal.SelectQuest(select);
            Frame();
        }

        void CloseJournal()
        {
            Tap(Key.J);
            Assert.That(_hud.QuestJournal.IsVisible, Is.False, "set-up: J closed the journal");
        }

        /// <summary>Flushes the session's input and returns what the transport holds.</summary>
        List<WireCommand> Sent()
        {
            _session.FlushInput();
            return _transport.TakeCommands();
        }

        List<WireCommand> Of(List<WireCommand> commands, CommandKind kind) { return commands.FindAll(c => c.Kind == kind); }

        /// <summary>The accepted row of 71 and its Abandon action, as the server delivers them (the row of TheCardClosesWhenTheServerConfirms).</summary>
        static QuestsState AcceptedWithAbandon(params WireQuestRow[] others)
        {
            var accepted = Offer(71, canAccept: false);
            accepted.State = QuestRowState.Open; accepted.OwnerAccepted = true;
            var rows = new List<WireQuestRow> { accepted };
            rows.AddRange(others);
            var state = Quests(rows.ToArray());
            state.View.Actions = new[] { new WireQuestAction { Quest = Near, Action = 1, Target = 0, Label = "Abandon" } };
            return state;
        }

        // ------------------------------------------------------------------ 1 and 2. the two surfaces share one hold

        [Test]
        public void JournalThenCardSendsOneAccept()
        {
            Deliver(Offer(71));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the journal press began a hold on the HUD's latch");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1), "the journal sent one QuestAccept");
            CloseJournal();

            OpenCard();
            Tap(Key.Enter);
            Assert.That(Of(_transport.TakeCommands(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "Enter sends nothing inside the hold");
            CardButton("Footer/Accept").onClick.Invoke();
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "the card's button sends nothing inside the hold");
            Assert.That(_hud.OfferCard.Accepting, Is.True, "the card is accepting");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPTING…"), "the card reads ACCEPTING");
            Assert.That(AcceptInteractable(), Is.False, "its button waits");
        }

        [Test]
        public void CardThenJournalSendsOneAccept()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold on the HUD's latch");
            Assert.That(Of(_transport.TakeCommands(), CommandKind.QuestAccept).Count, Is.EqualTo(1), "Enter sent one QuestAccept");

            OpenJournal(Near);
            Assert.That(JournalLabel("AcceptQuest"), Is.EqualTo("ACCEPTING…"), "the journal reads ACCEPTING");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.False, "its button waits");
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.QuestJournal.TryAccept(Near), Is.False, "TryAccept is refused inside the hold");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "the journal sent nothing");
        }

        // ------------------------------------------------------------------ 3 and 4. a dropped accept is offered again

        [Test]
        public void ADroppedAcceptIsOfferedAgainFromTheJournal()
        {
            Deliver(Offer(71));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press began a hold");
            JournalButton("AcceptQuest").onClick.Invoke();
            var first = Of(Sent(), CommandKind.QuestAccept);
            Assert.That(first.Count, Is.EqualTo(1), "a second press inside the hold sends nothing");

            _hud.TickOffer(2.25f);
            Frame();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "still held after 2.25 s");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.False, "not interactable");
            _hud.TickOffer(.25f);
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the hold ended at 2.5 s with no answer");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.True, "ACCEPT works again");
            Assert.That(JournalLabel("AcceptQuest"), Is.EqualTo("ACCEPT QUEST"));
            JournalButton("AcceptQuest").onClick.Invoke();
            var again = Of(Sent(), CommandKind.QuestAccept);
            Assert.That(again.Count, Is.EqualTo(1), "a press sends a second QuestAccept");
            Assert.That(again.Count == 1 && again[0].QuestRef.Equals(Near), Is.True, "for the same handle");
        }

        [Test]
        public void ADroppedAcceptIsOfferedAgainFromTheCard()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            Assert.That(Of(_transport.TakeCommands(), CommandKind.QuestAccept).Count, Is.EqualTo(1), "one QuestAccept");

            _hud.TickOffer(2.25f);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "still held after 2.25 s");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPTING…"));
            Tap(Key.Enter);
            Assert.That(Of(_transport.TakeCommands(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "Enter inside the hold sends nothing");
            _hud.TickOffer(.25f);
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the hold ended at 2.5 s");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPT · Enter"), "the caption returns");
            Assert.That(AcceptInteractable(), Is.True, "and the button works again");
            Tap(Key.Enter);
            var again = Of(_transport.TakeCommands(), CommandKind.QuestAccept);
            Assert.That(again.Count, Is.EqualTo(1), "Enter sends a second QuestAccept");
            Assert.That(again.Count == 1 && again[0].QuestRef.Equals(Near), Is.True, "for the same handle");
        }

        // ------------------------------------------------------------------ 5. the frame is the clock

        [Test]
        public void TheFrameAgesTheHoldWithTheJournalOpenAndTheCardClosed()
        {
            Deliver(Offer(71));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            Assert.That(_hud.OfferOpen, Is.False, "set-up: the card is closed");
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press began a hold");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1));

            MatchHud.OfferFrameDeltaOverride = .5f;
            var tickFrame = typeof(MatchHud).GetField("_offerTickFrame", All);
            Assert.That(tickFrame, Is.Not.Null, "the HUD keeps its once-a-frame guard _offerTickFrame");
            for (int i = 0; i < 4; i++)
            {
                tickFrame.SetValue(_hud, -1);
                Frame();
            }
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "after four .5 s frames the hold still runs");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.False, "and the button waits");
            tickFrame.SetValue(_hud, -1);
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the fifth frame ends it");
            Frame();
            Assert.That(JournalButton("AcceptQuest").interactable, Is.True, "one more frame and the button works");
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1), "a press sends a second QuestAccept");
        }

        // ------------------------------------------------------------------ 6 to 9. what ends a hold, and what does not

        [Test]
        public void AnUnrelatedDeliveryKeepsTheHold()
        {
            Deliver(Offer(71), Offer(72, giverX: 400));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press began a hold");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1));

            Deliver(Offer(71), Offer(72, giverX: 400, canAccept: false));
            Frame();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "a new view with the row unchanged keeps the hold");
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "a press sends nothing");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.False, "the button still waits");

            CloseJournal();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(Of(_transport.TakeCommands(), CommandKind.QuestAccept).Count, Is.EqualTo(0), "Enter sends nothing");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPTING…"), "the card reads ACCEPTING");
            _hud.TickOffer(2.5f);
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "2.5 s ends it");
        }

        [Test]
        public void ARowThatTurnedAcceptedEndsTheHold()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            var accepted = Offer(71, canAccept: false);
            accepted.State = QuestRowState.Open; accepted.OwnerAccepted = true;
            Deliver(accepted);
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the server's answer ended the hold");
        }

        [Test]
        public void ARowThatCanNoLongerBeAcceptedEndsTheHold()
        {
            Deliver(Offer(71));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press began a hold");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1));

            Deliver(Offer(71, canAccept: false));
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "a row that cannot be accepted ended the hold");
            Assert.That(JournalDetail("AcceptQuest").gameObject.activeSelf, Is.False, "ACCEPT is hidden");

            Deliver(Offer(71));
            Frame();
            Assert.That(JournalButton("AcceptQuest").interactable, Is.True, "acceptable again: the button works");
            Assert.That(JournalLabel("AcceptQuest"), Is.EqualTo("ACCEPT QUEST"));
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1), "a press sends a QuestAccept");
        }

        [Test]
        public void AGoneRowEndsTheHold()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            Deliver(Offer(72, giverX: 400));
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "a view without the row ended the hold");
        }

        // ------------------------------------------------------------------ 10 to 12. per handle, per session

        [Test]
        public void HoldsArePerHandle()
        {
            Deliver(Offer(71), Offer(72, giverX: 400));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press for 71 began a hold");
            Assert.That(Of(Sent(), CommandKind.QuestAccept).Count, Is.EqualTo(1));

            _hud.QuestJournal.SelectQuest(Far);
            Frame();
            Assert.That(JournalButton("AcceptQuest").interactable, Is.True, "72 can still be accepted");
            JournalButton("AcceptQuest").onClick.Invoke();
            var sent = Of(Sent(), CommandKind.QuestAccept);
            Assert.That(sent.Count, Is.EqualTo(1), "the press for 72 sends a QuestAccept");
            Assert.That(sent.Count == 1 && sent[0].QuestRef.Equals(Far), Is.True, "for 72");
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(2), "two holds");
        }

        [Test]
        public void ASessionChangeEndsTheHolds()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            var other = new Session(new FakeTransport(), new SessionOptions { Url = "ws://other/ws", Profile = "other", Now = () => 1000 });
            Invoke(_hud, "Bind", other);
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "binding another session ended the hold");
        }

        [Test]
        public void TheRewardHudResetEndsTheHolds()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            Invoke(_hud, "ResetQuestRewardHud");
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the reset ended the hold");
        }

        // ------------------------------------------------------------------ 13. the settled path

        [Test]
        public void ASettledRefreshDuringAHoldWritesNothingAndAllocatesNothing()
        {
            Deliver(Offer(71));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "the press began a hold");
            var label = JournalDetail("AcceptQuest/Label").GetComponent<TMP_Text>();
            label.text = "SENTINEL";
            Action refresh = _hud.QuestJournal.Refresh;
            refresh(); refresh(); refresh();
            Assert.That(label.text, Is.EqualTo("SENTINEL"), "a settled refresh writes no label");
            Assert.That(JournalButton("AcceptQuest").interactable, Is.False, "and the button still waits");
            AllocationMeter.AssertNone(refresh, 200, "200 settled journal refreshes during a hold");
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Refresh neither ages nor clears a hold");
        }

        // ------------------------------------------------------------------ 14 and 15. Abandon

        [Test]
        public void AHoldThatEndsNeitherArmsNorDisarmsAbandon()
        {
            DeliverItems(AcceptedWithAbandon(Offer(72, giverX: 400)));
            Frame();
            _transport.TakeCommands();
            OpenJournal(Far);
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Far), Is.True, "the press for 72 began a hold");
            _hud.QuestJournal.SelectQuest(Near);
            Frame();
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Abandon"), "71's Abandon stands unarmed");
            _hud.TickOffer(2.5f);
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the hold ended with no answer");
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Abandon"), "and armed nothing");

            _hud.QuestJournal.SelectQuest(Far);
            Frame();
            JournalButton("AcceptQuest").onClick.Invoke();
            Assert.That(_hud.AcceptLatch.IsHeld(Far), Is.True, "the second press for 72 began a hold");
            _hud.QuestJournal.SelectQuest(Near);
            Frame();
            JournalButton("AbandonQuest").onClick.Invoke();
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Confirm abandon"), "72's press blocks nothing of 71");
            _hud.TickOffer(2.5f);
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "the second hold ended too");
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Confirm abandon"), "and disarmed nothing");
            var sent = Sent();
            Assert.That(Of(sent, CommandKind.QuestAccept).Count, Is.EqualTo(2), "two QuestAccept");
            Assert.That(Of(sent, CommandKind.QuestAccept).TrueForAll(c => c.QuestRef.Equals(Far)), Is.True, "both for 72");
            Assert.That(Of(sent, CommandKind.QuestInteract).Count, Is.EqualTo(0), "no QuestInteract");
        }

        [Test]
        public void AbandonTakesNoPressInsideTheHoldOfAnAcceptThroughTheCard()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            DeliverItems(AcceptedWithAbandon());
            Frame();
            _transport.TakeCommands();
            OpenJournal(Near);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.False, "the answer ended the hold");
            Assert.That(_hud.AcceptLatch.PressedRecently(Near), Is.True, "but not the press stamp");
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Abandon"), "Abandon is shown, unarmed");
            JournalButton("AbandonQuest").onClick.Invoke();
            JournalButton("AbandonQuest").onClick.Invoke();
            Assert.That(Of(Sent(), CommandKind.QuestInteract).Count, Is.EqualTo(0), "two presses inside the stamp send nothing");
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Abandon"), "and arm nothing");

            _hud.TickOffer(QuestAcceptLatch.HoldSeconds);
            Assert.That(_hud.AcceptLatch.PressedRecently(Near), Is.False, "the stamp is over");
            JournalButton("AbandonQuest").onClick.Invoke();
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Confirm abandon"), "one press arms");
            Assert.That(Of(Sent(), CommandKind.QuestInteract).Count, Is.EqualTo(0), "and sends nothing");
            JournalButton("AbandonQuest").onClick.Invoke();
            var abandon = Of(Sent(), CommandKind.QuestInteract);
            Assert.That(abandon.Count, Is.EqualTo(1), "the second press sends one QuestInteract");
            Assert.That(abandon.Count == 1 && abandon[0].QuestAction == 1 && abandon[0].QuestTarget == 0 && abandon[0].QuestRef.Equals(Near), Is.True, "Abandon (1, 0) of 71");
            Assert.That(JournalLabel("AbandonQuest"), Is.EqualTo("Abandon"), "and disarms");
        }

        // ------------------------------------------------------------------ 16. a closed transport

        [Test]
        public void ACloseOfTheTransportEndsTheHoldsAndStamps()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Tap(Key.Enter);
            Assert.That(_hud.AcceptLatch.IsHeld(Near), Is.True, "Enter began a hold");
            _transport.Close();
            Frame();
            Assert.That(_hud.AcceptLatch.Held, Is.EqualTo(0), "a closed transport ended the hold");
            Assert.That(_hud.AcceptLatch.PressedRecently(Near), Is.False, "and the stamp");
        }

        // ------------------------------------------------------------------ input plumbing (QuestOfferEditModeTests, copied)

        // ------------------------------------------------------------------ input plumbing (UiInputRoutingEditModeTests, copied)

        void ReadBothAndFlush()
        {
            _hud.ReadUiInput();
            Invoke(_client, "ReadInput");
            _session.FlushInput();
        }

        void Press(params Key[] keys)
        {
            Assert.That(_keyboard != null && _keyboard.added && _keyboard.enabled, Is.True, "synthetic keyboard remains active");
            _keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            InputSystem.Update();
            Assert.That(Keyboard.current, Is.SameAs(_keyboard), "queued event must target the keyboard consumers read");
            foreach (var key in keys)
            {
                Assert.That(KeyControlFor(key).isPressed, Is.True, key + " state was not applied");
                Assert.That(KeyControlFor(key).wasPressedThisFrame, Is.True, key + " press edge was not observed this input update");
            }
        }

        void ReleaseKeys()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(Keyboard.current, Is.SameAs(_keyboard), "release event must reach the controlled keyboard");
            Assert.That(_keyboard.escapeKey.isPressed, Is.False);
            Assert.That(_keyboard.fKey.isPressed, Is.False);
            Assert.That(_keyboard.enterKey.isPressed, Is.False);
            Assert.That(_keyboard.tabKey.isPressed, Is.False);
        }

        KeyControl KeyControlFor(Key key)
        {
            switch (key)
            {
                case Key.C: return _keyboard.cKey;
                case Key.F: return _keyboard.fKey;
                case Key.J: return _keyboard.jKey;
                case Key.Digit1: return _keyboard.digit1Key;
                case Key.Space: return _keyboard.spaceKey;
                case Key.Enter: return _keyboard.enterKey;
                case Key.NumpadEnter: return _keyboard.numpadEnterKey;
                case Key.Escape: return _keyboard.escapeKey;
                case Key.Tab: return _keyboard.tabKey;
                case Key.W: return _keyboard.wKey;
                default: Assert.Fail("No injected-key precondition configured for " + key); return null;
            }
        }

        static object Field(object target, string name)
        {
            var f = target.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            return f != null ? f.GetValue(target) : null;
        }

        static object Invoke(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, All);
            Assert.That(info, Is.Not.Null, "missing method " + method);
            return info.Invoke(target, args);
        }
    }
}
