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
    /// P-UI-QUEST-OFFER-1 oracle: F near a quest giver opens a small offer card, Enter accepts, Escape or F closes it, and nothing
    /// stops the fight. One rig: the fixture of <c>UiInputRoutingEditModeTests</c> (a real MatchClient and MatchHud on a fake
    /// transport, a real keyboard): the player is 3 at (320, 320), alive, in an Active match, server time 1000. Quest views are
    /// delivered as encoded server messages. Time moves only through <c>OfferCard.Tick(dt)</c> (EditMode time does not advance).
    /// </summary>
    public sealed class QuestOfferEditModeTests
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
            _ownedEvents = new GameObject("Quest offer test EventSystem", typeof(UiInputRoutingEventSystem));
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

            _root = new GameObject("Quest offer fixture");
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
            return new Session(_transport, new SessionOptions { Url = "ws://quest-offer-test/ws", Profile = "offer-test", Now = () => 1000 });
        }

        [TearDown]
        public void TearDown()
        {
            if (_cleanupFinished) return;
            _cleanupFinished = true;
            try
            {
                UiMotion.ReducedOverride = _priorReduced;
                foreach (var saved in _scaleBackup)
                    if (saved.Key != null) typeof(ShowcaseSettings).GetField("_textScale", All).SetValue(saved.Key, saved.Value);
                _scaleBackup.Clear();
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
            var t = path.StartsWith("Footer/") ? card.Find(path) : card.Find("Description/Viewport/Content/" + path);
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
            var box = card.Find("Description/Viewport/Content/" + boxName);
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

        Transform Launcher() { return HudTestPaths.Find(_hud.transform, LauncherPath); }

        bool LauncherActive() { var l = Launcher(); return l != null && l.gameObject.activeInHierarchy; }

        string LauncherCaption()
        {
            var l = Launcher();
            Assert.That(l, Is.Not.Null, "the quest action launcher exists");
            var label = l != null ? l.GetComponentInChildren<TMP_Text>(true) : null;
            return label != null ? label.text : "";
        }

        bool LauncherInteractable()
        {
            var l = Launcher();
            var button = l != null ? l.GetComponent<Button>() : null;
            return button != null && button.interactable;
        }

        void ClickLauncher()
        {
            var l = Launcher();
            Assert.That(l, Is.Not.Null, "the quest action launcher exists");
            if (l != null) l.GetComponent<Button>().onClick.Invoke();
        }

        // ------------------------------------------------------------------ 1. F opens the card

        [Test]
        public void FOpensTheOfferCardWhenAnOfferIsInReach()
        {
            Deliver(Offer(71));
            Frame();
            Assert.That(_hud.OfferOpen, Is.False, "set-up: the card starts closed");
            _transport.TakeCommands();

            Tap(Key.F);

            Assert.That(_hud.OfferOpen, Is.True, "F opens the card");
            Assert.That(_hud.OfferCard.IsOpen, Is.True, "the card says it is open");
            Assert.That(_hud.OfferCard.Shown, Is.EqualTo((QuestHandle?)Near), "it shows the offer in reach");
            Assert.That(CardText("Giver/Label"), Is.EqualTo("Old Marta"));
            Assert.That(CardText("Name/Label"), Is.EqualTo("Clear the Barrow"));
            Assert.That(CardText("Objective/Label"), Is.EqualTo("Bring back three lanterns"));
            Assert.That(CardText("Reward/Label"), Is.EqualTo("Reward · Hook"));
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPT · Enter"));
            Assert.That(CardText("Footer/Close/Label"), Is.EqualTo("Esc · close"));
            Assert.That(Count(_transport.TakeCommands(), CommandKind.Pickup), Is.EqualTo(0), "talking picks nothing up");

            var close = CardButton("Footer/Close");
            if (close != null) close.onClick.Invoke();
            Assert.That(_hud.OfferOpen, Is.False, "the Close button closes the card");
            Assert.That(_hud.OfferCard.IsOpen, Is.False);
        }

        // ------------------------------------------------------------------ 2. F still picks up

        [Test]
        public void FStillPicksUpWhenAPropIsInReach()
        {
            DeliverItems(PickupInReach(true), Quests(Offer(71)));
            Frame();
            Assert.That(_session.Collections.Focus.HasValue, Is.True, "set-up: a pickup is in reach");
            _transport.TakeCommands();

            Tap(Key.F);

            var commands = _transport.TakeCommands();
            Assert.That(Count(commands, CommandKind.Pickup), Is.EqualTo(1), "F sends the pickup");
            Assert.That(_hud.OfferOpen, Is.False, "and the card stays closed");

            // the same key opens the card once the prop is out of reach
            DeliverItems(PickupInReach(false), Quests(Offer(71)));
            Frame();
            Assert.That(_session.Collections.Focus.HasValue, Is.False, "set-up: the pickup is gone");
            Tap(Key.F);
            Assert.That(_hud.OfferOpen, Is.True, "F talks when nothing is in reach to pick up");
            Assert.That(Count(_transport.TakeCommands(), CommandKind.Pickup), Is.EqualTo(0), "talking picks nothing up");
        }

        // ------------------------------------------------------------------ 3. Enter accepts once

        [TestCase(Key.Enter)]
        [TestCase(Key.NumpadEnter)]
        public void EnterAcceptsOnce(Key enter)
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();

            Tap(enter);
            Frame();

            var commands = _transport.TakeCommands();
            Assert.That(Count(commands, CommandKind.QuestAccept), Is.EqualTo(1), enter + " sends one QuestAccept");
            var sent = commands.Find(c => c.Kind == CommandKind.QuestAccept);
            Assert.That(sent != null && sent.QuestRef.Equals(Near), Is.True, "for the shown handle");
            Assert.That(_hud.OfferCard.Accepting, Is.True, "the card is accepting");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPTING…"));
            Assert.That(AcceptInteractable(), Is.False, "the button waits");

            Tap(enter);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.QuestAccept), Is.EqualTo(0), "a second Enter sends nothing");

            _hud.TickOffer(2.6f);
            Assert.That(_hud.OfferCard.Accepting, Is.False, "the wait is over after 2.5 s");
            Assert.That(CardText("Footer/Accept/Label"), Is.EqualTo("ACCEPT · Enter"), "the offer is still acceptable: the caption returns");
            Assert.That(AcceptInteractable(), Is.True, "and the button works again");
        }

        // ------------------------------------------------------------------ 4. the server confirms

        [Test]
        public void TheCardClosesWhenTheServerConfirms()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();

            var accepted = Offer(71, canAccept: false);
            accepted.State = QuestRowState.Open; accepted.OwnerAccepted = true;
            Deliver(accepted);
            Frame();

            Assert.That(_hud.OfferOpen, Is.False, "an OwnerAccepted row closes the card at once");
            Assert.That(_hud.OfferCard.IsOpen, Is.False);
            var card = Card();
            var group = card != null ? card.GetComponent<CanvasGroup>() : null;
            Assert.That(group, Is.Not.Null, "the card root carries the HudFade CanvasGroup");
            if (group != null) Assert.That(group.blocksRaycasts, Is.False, "a closed card takes no pointer");
        }

        // ------------------------------------------------------------------ 5. leaving reach

        [Test]
        public void TheCardClosesHalfASecondAfterTheOfferLeavesReach()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();

            Deliver(Offer(71, canAccept: false));
            Frame();
            Assert.That(_hud.OfferOpen, Is.True, "the card stays through the grace");
            Assert.That(AcceptInteractable(), Is.False, "but its Accept cannot be pressed");
            Tap(Key.Enter);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.QuestAccept), Is.EqualTo(0), "Enter sends nothing out of reach");

            _hud.OfferCard.Tick(.6f);
            Assert.That(_hud.OfferCard.Lapsed, Is.True, "0.6 s out of reach is past the 0.5 s grace");
            Invoke(_hud, "LateUpdate");
            Assert.That(_hud.OfferOpen, Is.False, "the HUD closes a lapsed card");
        }

        // ------------------------------------------------------------------ 6. a short loss of reach

        [Test]
        public void AShortLossOfReachKeepsTheCard()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();

            Deliver(Offer(71, canAccept: false));
            Frame();
            _hud.OfferCard.Tick(.2f);
            Deliver(Offer(71));
            Frame();

            Assert.That(_hud.OfferOpen, Is.True, "back in reach inside the grace: the card stays");
            Assert.That(_hud.OfferCard.Lapsed, Is.False, "and regaining reach zeroed the grace");
            Assert.That(AcceptInteractable(), Is.True, "Accept works again");
            _hud.OfferCard.Tick(.4f);
            Assert.That(_hud.OfferCard.Lapsed, Is.False, "regaining reach stopped the clock: later ticks add nothing");
            Tap(Key.Enter);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.QuestAccept), Is.EqualTo(1), "Enter sends exactly one QuestAccept");
        }

        // ------------------------------------------------------------------ 7. Escape and F

        [Test]
        public void EscapeAndFCloseIt()
        {
            Deliver(Offer(71));
            Frame();
            Tap(Key.C);
            Assert.That(_hud.ChestOwnsDigits, Is.True, "set-up: C gave the chest panel the digits");
            OpenCard();

            Tap(Key.Escape);
            Assert.That(_hud.OfferOpen, Is.False, "Escape closes the card");
            Assert.That(_hud.ChestOwnsDigits, Is.True, "and nothing else reacted to that Escape");

            Tap(Key.Escape);
            Assert.That(_hud.ChestOwnsDigits, Is.False, "a second Escape releases the chest focus");

            OpenCard();
            Tap(Key.F);
            Assert.That(_hud.OfferOpen, Is.False, "F with the card open closes it");
            Assert.That(Count(_transport.TakeCommands(), CommandKind.Pickup), Is.EqualTo(0), "and picks nothing up");
        }

        // ------------------------------------------------------------------ 8. play goes on

        [Test]
        public void TheCardDoesNotStopPlay()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Assert.That(_hud.BlocksKeyboardInput, Is.False, "an open card does not block the keyboard");
            Assert.That(_hud.BlocksPointerInput, Is.False, "nor the pointer");
            var card = Card();
            var group = card != null ? card.GetComponent<CanvasGroup>() : null;
            Assert.That(group, Is.Not.Null, "the card root carries the HudFade CanvasGroup");
            if (group != null) Assert.That(group.blocksRaycasts, Is.True, "an open card takes a click so that it does not attack");

            Tap(Key.Digit1);
            Assert.That(_transport.TakeCommands(), Is.Empty, "a digit does nothing");
            Tap(Key.Space);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.Swap), Is.EqualTo(1), "Space still swaps");
            Tap(Key.W);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.Move), Is.GreaterThan(0), "W still moves");
            Assert.That(_hud.OfferOpen, Is.True, "and the card stayed open through it all");

            _hud.OfferCard.Close();
            if (group != null) Assert.That(group.blocksRaycasts, Is.False, "a closed card takes no pointer");
        }

        // ------------------------------------------------------------------ 9. where it sits

        struct Rect4
        {
            public float Left, Top, Width, Height;
            public float Right => Left + Width;
            public float Bottom => Top + Height;
            public HudStageRect Stage => new HudStageRect(Left, Top, Width, Height);
        }

        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Top = h / 2f - max.y, Width = max.x - min.x, Height = max.y - min.y };
        }

        RectTransform Stage() { return (RectTransform)HudTestPaths.Find(_hud.transform, "MatchUI/HudStage"); }

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Field(_hud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Field(_hud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        /// <summary>The card as HudRegions.OfferCard and QuestOffer.Lift place it from the live values.</summary>
        HudStageRect ExpectedCard()
        {
            float hudHeight = ((RectTransform)Field(_hud, "_hud")).rect.height;
            float dockTop = (float)Invoke(_hud, "PlateChainBottom") + HudRegions.TrackerGap;
            float floor = (float)Invoke(_hud, "TrackerFloor");
            int rows = ((QuestTrackerView)Field(_hud, "_questTracker")).Rows;
            var region = HudRegions.OfferCard(hudHeight, dockTop, rows, _hud.OfferCard.WantedHeight, floor + HudRegions.TrackerGap);
            var dock = HudRegions.TrackerDock(dockTop, rows);
            float top = QuestOffer.Lift(region.Y, region.Height, dock.Y + dock.Height, floor);
            return new HudStageRect(region.X, top, region.Width, region.Height);
        }

        void AssertCardPlaced(RectTransform stage, string when)
        {
            var card = Card();
            Assert.That(card, Is.Not.Null, "the card exists " + when);
            if (card == null) return;
            var m = Measure(stage, (RectTransform)card);
            var expected = ExpectedCard();
            Assert.That(m.Width, Is.EqualTo(HudRegions.OfferWidth).Within(Eps), "uses the shared offer width " + when);
            Assert.That(m.Left, Is.EqualTo(HudRegions.Margin).Within(Eps), "12 from the left " + when);
            Assert.That(m.Left, Is.EqualTo(expected.Left).Within(Eps), "left " + when);
            Assert.That(m.Top, Is.EqualTo(expected.Top).Within(Eps), "top " + when);
            Assert.That(m.Height, Is.EqualTo(expected.Height).Within(Eps), "height " + when);
            var tracker = HudTestPaths.Find(_hud.transform, "MatchUI/HudStage/PlayerHUD/QuestTracker");
            if (tracker != null && tracker.gameObject.activeInHierarchy)
                Assert.That(m.Stage.Overlaps(Measure(stage, (RectTransform)tracker).Stage), Is.False, "clear of the quest tracker " + when);
            var champion = HudTestPaths.Find(_hud.transform, "MatchUI/HudStage/PlayerHUD/ChampionCombatHud");
            if (champion != null && champion.gameObject.activeInHierarchy)
                Assert.That(m.Stage.Overlaps(Measure(stage, (RectTransform)champion).Stage), Is.False, "clear of the champion panel " + when);
        }

        [TestCaseSource(nameof(Windows))]
        public void TheCardSitsOnTheLeftClearOfTheTrackerTheReadoutsAndTheChampionPanel(Vector2 window)
        {
            Invoke(_hud, "SetPlateMode", true);
            Layout(window);
            var stage = Stage();
            Deliver(Offer(71), Running(72));
            Frame(); Frame();
            Assert.That(((QuestTrackerView)Field(_hud, "_questTracker")).Rows, Is.EqualTo(1), "set-up: the tracker lists the running quest");
            OpenCard();
            Frame(); Frame();

            AssertCardPlaced(stage, "(plate without extras)");
            var card = Card();
            if (card != null)
            {
                var zone = HudRegions.CombatZone(stage.rect.width, stage.rect.height);
                var m = Measure(stage, (RectTransform)card);
                Assert.That(m.Right, Is.LessThanOrEqualTo(zone.Left + Eps), "left of the combat zone");
            }

            float chainClosed = (float)Invoke(_hud, "PlateChainBottom");
            Invoke(_hud, "ToggleExtras");
            Frame(); Frame();
            Assert.That((float)Invoke(_hud, "PlateChainBottom"), Is.GreaterThan(chainClosed), "set-up: the open extras push the plate chain down");
            AssertCardPlaced(stage, "(plate extras open)");
            Invoke(_hud, "ToggleExtras");
            Frame(); Frame();
            Assert.That((float)Invoke(_hud, "PlateChainBottom"), Is.EqualTo(chainClosed).Within(Eps), "set-up: the extras are closed again");

            // The strip of readouts sets the floor: with the extras closed there is room for the minimum card above it.
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new ReadoutsState { Rows = new[] {
                new WireReadout { Card = 80, Kind = 0, Value = 2, Max = 5 } } } } });
            _session.Poll();
            Frame(); Frame(); Frame();
            var strip = (TMP_Text)Field(_hud, "_readoutStrip");
            Assert.That(strip, Is.Not.Null, "set-up: the readout strip was built");
            Assert.That(strip != null && !string.IsNullOrEmpty(strip.text), Is.True, "set-up: the strip has text");
            if (strip == null) return;
            float stripTop = Measure(stage, strip.rectTransform).Top;
            var dock = HudRegions.TrackerDock((float)Invoke(_hud, "PlateChainBottom") + HudRegions.TrackerGap, 1);
            float floor = (float)Invoke(_hud, "TrackerFloor");
            Assert.That(floor - (dock.Y + dock.Height), Is.GreaterThanOrEqualTo(HudRegions.OfferMinHeight),
                "set-up: at least the minimum card height lies between the tracker and the floor");
            AssertCardPlaced(stage, "(readout strip showing)");
            if (card != null)
                Assert.That(Measure(stage, (RectTransform)card).Bottom, Is.LessThanOrEqualTo(stripTop - 8f + Eps), "the card ends 8 above the readout strip");
        }

        // ------------------------------------------------------------------ 10. the prompt lane

        [Test]
        public void ThePromptLaneOffersTheTalk()
        {
            Deliver(Offer(71));
            Frame();
            Assert.That(LauncherActive(), Is.True, "an offer in reach shows the launcher");
            Assert.That(LauncherInteractable(), Is.True, "and it can be pressed");
            Assert.That(LauncherCaption(), Is.EqualTo("F · Talk to Old Marta"));
            ClickLauncher();
            Assert.That(_hud.OfferOpen, Is.True, "a click on the launcher opens the card");
            _hud.OfferCard.Close();
            Frame();

            DeliverItems(PickupInReach(true), Quests(Offer(71)));
            Frame();
            var pill = HudTestPaths.Find(_hud.transform, "MatchUI/HudStage/PlayerHUD/CombatActions/Pickup");
            Assert.That(pill != null && pill.gameObject.activeInHierarchy, Is.True, "set-up: the pickup pill shows");
            Assert.That(LauncherActive(), Is.True, "the pill does not hide the launcher");
            Assert.That(LauncherCaption(), Is.EqualTo("Talk to Old Marta"), "beside the pill the caption drops the key");

            DeliverItems(PickupInReach(false), Quests(Offer(71, canAccept: false)));
            Frame();
            Assert.That(LauncherActive(), Is.True, "out of reach the prompt holds");
            Assert.That(LauncherInteractable(), Is.False, "but cannot be pressed");
            _hud.OfferCard.Tick(.6f);
            Frame();
            Assert.That(LauncherActive(), Is.False, "half a second later it is gone");
        }

        // ------------------------------------------------------------------ 11. the reward choice wins the caption

        [Test]
        public void TheRewardChoiceCaptionStillWins()
        {
            DeliverItems(WithChoice(Offer(71)));
            Frame();
            var overlay = HudTestPaths.Find(_hud.transform, "MatchUI/QuestRewardChoiceOverlay");
            Assert.That(overlay != null && overlay.gameObject.activeSelf, Is.True, "set-up: the pending choice opens its modal");
            Assert.That(_hud.TryInteract(), Is.False, "F opens nothing over the modal");
            Assert.That(_hud.OfferOpen, Is.False, "nothing opened");
            var later = overlay != null ? overlay.Find("QuestRewardChoice/ChooseLater") : null;
            Assert.That(later, Is.Not.Null, "the modal has ChooseLater");
            if (later != null) later.GetComponent<Button>().onClick.Invoke();
            Frame();
            Assert.That(overlay != null && overlay.gameObject.activeSelf, Is.False, "set-up: the modal is deferred");

            Assert.That(LauncherActive(), Is.True, "the launcher shows");
            Assert.That(LauncherCaption(), Is.EqualTo("Choose quest reward"), "the pending choice keeps the caption");
            ClickLauncher();
            Assert.That(overlay != null && overlay.gameObject.activeSelf, Is.True, "a click opens the modal, not the offer card");
            Assert.That(_hud.OfferOpen, Is.False, "the click opened the reward modal, not the offer card");

            if (later != null) later.GetComponent<Button>().onClick.Invoke();
            Frame();
            Assert.That(_hud.TryInteract(), Is.True, "the offer is still reachable with F while the choice is deferred");
            Assert.That(_hud.OfferOpen, Is.True, "the card is open");
            Assert.That(LauncherCaption(), Is.EqualTo("Choose quest reward"), "and the caption is not rewritten");
        }

        // ------------------------------------------------------------------ 12. accept checks the current view

        [Test]
        public void AcceptChecksTheCurrentView()
        {
            // a replacement view without the row: the card closes and Enter sends nothing
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Deliver(Offer(72, giverX: 400));
            Frame();
            Assert.That(_hud.OfferOpen, Is.False, "a view without the shown row closes the card");
            Tap(Key.Enter);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.QuestAccept), Is.EqualTo(0), "Enter sends nothing");

            // a replacement view that still has the row: the card stays and Enter sends one accept
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Deliver(Offer(71, name: "The Barrow, Renamed"));
            Frame();
            Assert.That(_hud.OfferOpen, Is.True, "a new view object with the same row keeps the card");
            Assert.That(CardText("Name/Label"), Is.EqualTo("The Barrow, Renamed"), "and shows the new facts");
            Tap(Key.Enter);
            var commands = _transport.TakeCommands();
            Assert.That(Count(commands, CommandKind.QuestAccept), Is.EqualTo(1), "Enter sends one QuestAccept");
            var sent = commands.Find(c => c.Kind == CommandKind.QuestAccept);
            Assert.That(sent != null && sent.QuestRef.Equals(Near), Is.True, "for the handle");

            // a second session: the card is closed and the old card cannot accept for it
            var accepted = Offer(71, canAccept: false);
            accepted.State = QuestRowState.Open; accepted.OwnerAccepted = true;
            Deliver(accepted);
            Frame();
            Deliver(Offer(71));
            Frame();
            OpenCard();
            var other = new Session(new FakeTransport(), new SessionOptions { Url = "ws://other/ws", Profile = "other", Now = () => 1000 });
            Invoke(_hud, "Bind", other);
            Assert.That(_hud.OfferOpen, Is.False, "binding another session closes the card");
            Tap(Key.Enter);
            Assert.That(Count(_transport.TakeCommands(), CommandKind.QuestAccept), Is.EqualTo(0), "and Enter sends nothing");
        }

        // ------------------------------------------------------------------ 13. never over a modal or the map

        [Test]
        public void ItNeverOpensOverAModalOrTheMap()
        {
            Deliver(Offer(71));
            Frame();

            // the journal
            Tap(Key.J);
            Assert.That(_hud.QuestJournal.IsVisible, Is.True, "set-up: J opened the journal");
            Assert.That(_hud.TryInteract(), Is.False, "F opens nothing over the journal");
            Tap(Key.F);
            Assert.That(_hud.OfferOpen, Is.False, "not by the key either");
            Tap(Key.J);
            Assert.That(_hud.QuestJournal.IsVisible, Is.False, "set-up: the journal is closed");
            OpenCard();
            Tap(Key.J);
            Frame();
            Assert.That(_hud.QuestJournal.IsVisible, Is.True, "set-up: the journal opened over the card");
            Assert.That(_hud.OfferOpen, Is.False, "opening the journal closes the card");
            Tap(Key.J);

            // the Tab map (held)
            Press(Key.Tab, Key.F);
            ReadBothAndFlush();
            Assert.That(_hud.TacticalMap.IsOpen, Is.True, "set-up: Tab opens the map");
            Assert.That(_hud.OfferOpen, Is.False, "F opens nothing while the map is up");
            ReleaseKeys();
            _hud.ReadUiInput();
            Assert.That(_hud.TacticalMap.IsOpen, Is.False, "set-up: the map closed on release");
            OpenCard();
            Press(Key.Tab);
            ReadBothAndFlush();
            Frame();
            Assert.That(_hud.TacticalMap.IsOpen, Is.True, "set-up: the map opened over the card");
            Assert.That(_hud.OfferOpen, Is.False, "opening the map closes the card");
            ReleaseKeys();
            _hud.ReadUiInput();

            // the reward-choice modal, by delivery
            OpenCard();
            DeliverItems(WithChoice(Offer(71)));
            Frame();
            var overlay = HudTestPaths.Find(_hud.transform, "MatchUI/QuestRewardChoiceOverlay");
            Assert.That(overlay != null && overlay.gameObject.activeSelf, Is.True, "set-up: the choice modal opened over the card");
            Assert.That(_hud.OfferOpen, Is.False, "the modal closes the card");
            Assert.That(_hud.TryInteract(), Is.False, "and F opens nothing over it");
        }

        // ------------------------------------------------------------------ 14. text scale

        static bool Wraps(TMP_Text t) => t.textWrappingMode != TextWrappingModes.NoWrap;

        readonly List<KeyValuePair<ShowcaseSettings, float>> _scaleBackup = new List<KeyValuePair<ShowcaseSettings, float>>();

        /// <summary>Sets the text scale the way HudTextScaleEditModeTests does, on the HUD's own settings and on the current ones (the card reads
        /// ShowcaseSettings.Current, MatchHud.ApplyTextScale reads the HUD's), then applies it.</summary>
        void SetTextScale(float scale)
        {
            var own = (ShowcaseSettings)Field(_hud, "_showcaseSettings");
            Assert.That(own, Is.Not.Null, "the HUD has its ShowcaseSettings");
            if (own == null) return;
            if (ShowcaseSettings.Current == null) typeof(ShowcaseSettings).GetMethod("Awake", All).Invoke(own, null);
            Assert.That(ShowcaseSettings.Current, Is.Not.Null, "ShowcaseSettings.Current is set");
            var field = typeof(ShowcaseSettings).GetField("_textScale", All);
            foreach (var settings in new[] { own, ShowcaseSettings.Current })
            {
                if (settings == null) continue;
                if (!_scaleBackup.Exists(p => ReferenceEquals(p.Key, settings))) _scaleBackup.Add(new KeyValuePair<ShowcaseSettings, float>(settings, (float)field.GetValue(settings)));
                field.SetValue(settings, scale);
            }
            Invoke(_hud, "ApplyTextScale");
            Invoke(_hud, "LateUpdate");
        }

        void AssertLabelsFit(string when)
        {
            var card = Card();
            Assert.That(card, Is.Not.Null, "the card exists " + when);
            if (card == null) return;
            var problems = new List<string>();
            foreach (var label in card.GetComponentsInChildren<TMP_Text>(true))
            {
                string name = label.transform.parent.name + "/" + label.name;
                if (label.GetComponent<MythicTextScale>() == null) problems.Add(name + " has no MythicTextScale");
                if (label.overflowMode == TextOverflowModes.Ellipsis || label.overflowMode == TextOverflowModes.Truncate)
                    problems.Add(name + " would be cut (overflow " + label.overflowMode + ")");
                if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
                var rect = label.rectTransform.rect;
                bool auto = label.enableAutoSizing; float keep = label.fontSize;
                label.enableAutoSizing = false;
                var need = label.GetPreferredValues(label.text, Wraps(label) ? rect.width : 0f, 0f);
                label.fontSize = keep; label.enableAutoSizing = auto;
                float widthShare = label.GetComponent<MythicTextScale>().FitBox ? MythicTextScale.WidthShare : 1f;
                if (need.x > rect.width * widthShare + .01f || need.y > rect.height + .01f)
                    problems.Add(name + " '" + label.text + "' at " + keep + " needs " + need.x.ToString("0.#") + "x" + need.y.ToString("0.#")
                        + " of " + rect.width.ToString("0.#") + "x" + rect.height.ToString("0.#"));
            }
            Assert.That(problems, Is.Empty, "labels " + when + ":\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheTextFitsAtTextScaleOnePointFive()
        {
            string objective = "Carry the sealed letters to each of the three watchtowers before the horn sounds";
            string reward = "One hundred gold, a Tornado charge and the Warden's goodwill";
            Assert.That(objective.Length, Is.EqualTo(80), "set-up: the objective is 80 characters");
            Assert.That(reward.Length, Is.EqualTo(60), "set-up: the reward is 60 characters");
            Deliver(Offer(71, objective: objective, reward: reward));
            Frame();
            OpenCard();
            Frame();
            foreach (string box in new[] { "Name", "Objective", "Reward" })
            {
                var label = CardChild(box + "/Label");
                var text = label != null ? label.GetComponent<TMP_Text>() : null;
                Assert.That(text != null && Wraps(text), Is.True, box + " wraps");
            }
            Assert.That(CardText("Objective/Label"), Is.EqualTo(objective), "the objective is shown whole");
            Assert.That(CardText("Reward/Label"), Is.EqualTo("Reward · " + reward), "the reward is shown whole");
            AssertLabelsFit("at text scale 1");
            float height = _hud.OfferCard.WantedHeight;
            Assert.That(height, Is.GreaterThan(0f), "the card wants a height");

            SetTextScale(1.5f);
            Frame();
            AssertLabelsFit("at text scale 1.5");
            Assert.That(_hud.OfferCard.WantedHeight, Is.GreaterThan(height), "bigger text wants a taller card");

            _hud.OfferCard.Place(new HudRegion(HudRegionAnchor.TopLeft, 12f, 120f, HudRegions.OfferWidth, 148f));
            var scroll = Card().Find("Description").GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.vertical, Is.True, "the full copy scrolls in a crowded dock");
            Assert.That(CardChild("Objective/Label").GetComponent<TMP_Text>().fontSize, Is.GreaterThanOrEqualTo(24f));
            Assert.That(CardButton("Footer/Accept").gameObject.activeInHierarchy, Is.True);
            AssertLabelsFit("with the body masked and actions pinned");
        }

        // ------------------------------------------------------------------ 15. the Ends and More lines

        [Test]
        public void TheEndsAndMoreLines()
        {
            Deliver(Offer(71, expiresAt: 0));
            Frame();
            OpenCard();
            Assert.That(CardTextShown("Ends"), Is.False, "no deadline: no Ends line");
            Assert.That(CardTextShown("More"), Is.False, "one offer: no More line");

            Deliver(Offer(71, expiresAt: 10000));
            Frame();
            Assert.That(CardText("Ends/Label"), Is.EqualTo("Ends in 0:09"), "9 s left at server time 1000");

            Deliver(Offer(71, expiresAt: 999));
            Frame();
            Assert.That(CardText("Ends/Label"), Is.EqualTo("Ends in 0:00"), "a deadline already passed reads 0:00");

            Deliver(Offer(71), Offer(72, giverX: 400));
            Frame();
            Assert.That(_hud.OfferOpen, Is.True, "set-up: the nearer offer stays");
            Assert.That(CardText("More/Label"), Is.EqualTo("2 offers here · J for all"));

            Deliver(Offer(71));
            Frame();
            Assert.That(CardTextShown("More"), Is.False, "back to one offer: the More line is hidden");
        }

        // ------------------------------------------------------------------ 16. no garbage

        Action RefreshDelegate()
        {
            return (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));
        }

        [Test]
        public void AClosedCardWithAnOfferInReachAllocatesNothing()
        {
            Deliver(Offer(71));
            Frame(); Frame();
            Assert.That(LauncherActive(), Is.True, "set-up: the launcher shows the offer in reach");
            Assert.That(_hud.OfferOpen, Is.False, "set-up: the card is closed");
            var refresh = RefreshDelegate();
            for (int i = 0; i < 5; i++) refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 steady refresh frames with a closed card and an offer in reach");
        }

        [Test]
        public void AnOpenCardAllocatesNothing()
        {
            Deliver(Offer(71));
            Frame();
            OpenCard();
            Frame(); Frame();
            Assert.That(CardText("Ends/Label"), Is.EqualTo("Ends in 0:09"), "set-up: the open card draws its facts");
            var refresh = RefreshDelegate();
            for (int i = 0; i < 5; i++) refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 steady refresh frames with the card open");
        }

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
