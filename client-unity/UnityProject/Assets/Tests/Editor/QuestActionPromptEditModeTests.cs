using System;
using System.Collections.Generic;
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
    /// P-UI-QUEST-ACTION-PROMPT-1: the prompt above the action row names the quest action that is in reach. With exactly one nearby
    /// action the launcher caption reads "label · quest name" ("Meditate · Stay A While"), the label alone for action 9 or a quest
    /// without a name, "Nearby quest actions · n" for two or more (and for one action with an empty label); the nearby-actions window
    /// reads the same words in the same order; the caption is built when the actions are bound (no allocation in a steady frame), a
    /// reset leaves no caption behind, and the longest caption fits the launcher's box at both text sizes. A whole MatchHud on a real
    /// MatchClient over a fake transport (the rig of AbilityRowNearbyNoGarbageEditModeTests), no keyboard, no clock: every snapshot
    /// carries server time 1000.
    /// </summary>
    public sealed class QuestActionPromptEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string LauncherPath = "PlayerHUD/QuestRewards/QuestActionLauncher";
        const string CountOne = "Nearby quest actions · 1";
        static readonly Vector2 N2 = new Vector2(640, 600);

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
            _events = new GameObject("Action prompt events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Action prompt fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://prompt/ws", Profile = "prompt", Now = () => 1000 });
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

        // ------------------------------------------------------------------ deliveries

        static QuestsState Quests(WireQuestRow[] rows = null, WireQuestAction[] actions = null, WireQuestDrop[] drops = null)
        {
            var view = new WireQuestView();
            if (rows != null) view.Rows = rows;
            if (actions != null) view.Actions = actions;
            if (drops != null) view.Drops = drops;
            return new QuestsState { Available = true, View = view };
        }

        /// <summary>An accepted, running quest row (every string the codec requires is set; copies Running of QuestOfferEditModeTests).</summary>
        static WireQuestRow AcceptedRow(uint handle, string def, string name) => new WireQuestRow
        {
            Handle = new QuestHandle(handle), Def = def, GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = 61000,
            OwnerAccepted = true, CanAccept = false, Name = name, GiverLabel = "Giver", Brief = "brief", Objective = "objective", Reward = "Tornado",
        };

        /// <summary>An offer row in reach (copies Offer of QuestOfferEditModeTests: Preview, CanAccept).</summary>
        static WireQuestRow OfferRow(uint handle) => new WireQuestRow
        {
            Handle = new QuestHandle(handle), Def = "fixture", GiverX = 330, GiverY = 320, State = QuestRowState.Preview, ExpiresAt = 10000,
            CanAccept = true, Name = "Clear the Barrow", GiverLabel = "Old Marta", Brief = "brief", Objective = "Bring back three lanterns", Reward = "Hook",
        };

        static WireQuestAction Act(uint quest, byte action, string label, byte target = 0) => new WireQuestAction
        {
            Quest = new QuestHandle(quest), Action = action, Target = target, Label = label, Points = new[] { 64.0, 80.0 },
        };

        static WireQuestAction Abandon(uint quest) => Act(quest, 1, "Abandon");
        static WireQuestAction Meditate() => Act(71, 6, "Meditate");

        /// <summary>One accepted row and its actions.</summary>
        static QuestsState Accepted(uint handle, string def, string name, params WireQuestAction[] actions) =>
            Quests(new[] { AcceptedRow(handle, def, name) }, actions);

        static QuestsState MeditateState() => Accepted(71, "stay_a_while", "Stay A While", Abandon(71), Meditate());
        static QuestsState AbandonOnly() => Accepted(71, "stay_a_while", "Stay A While", Abandon(71));

        /// <summary>Rows 71 (Stay A While) and 72 (Smuggler's Run) in ascending handle order, with the actions of the two-row tests.</summary>
        static QuestsState TwoRows(params WireQuestAction[] extra)
        {
            var actions = new List<WireQuestAction> { Abandon(71), Meditate(), Abandon(72), Act(72, 3, "Take the parcel") };
            actions.AddRange(extra);
            return Quests(new[] { AcceptedRow(71, "stay_a_while", "Stay A While"), AcceptedRow(72, "smugglers_run", "Smuggler's Run") },
                actions.ToArray(), extra.Length == 0 ? null : new[] { new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = 64, Y = 64 } });
        }

        /// <summary>The lone drop action 9 (handle 71, target 37, no row).</summary>
        static QuestsState PickupOnly() => Quests(actions: new[] { Act(71, 9, "Pick up WallBuster", 37) },
            drops: new[] { new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = 64, Y = 64 } });

        static PropsState PickupInReach(bool inReach) => new PropsState
        {
            Rows = new[] { new WireProp { Handle = new PickupHandle(5), X = 300, Y = 300, Kind = PropKind.Ground, GroundWeapon = (byte)WeaponId.Axe } },
            Focus = inReach ? new WireFocus { Handle = new PickupHandle(5) } : (WireFocus?)null,
        };

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
        RectTransform Launcher => Child(FullStage, LauncherPath);
        string LauncherCaption() => Launcher.GetComponentInChildren<TMP_Text>(true).text;
        int Count => (int)GetF(_hud, "_nearbyQuestCount");
        string Cached => (string)GetF(_hud, "_nearbyCaption");
        Action Refresh() => (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

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

        /// <summary>Deliver a quest state in a new snapshot and run two frames.</summary>
        void Deliver(uint tick, params StateItem[] items) { Snap(tick, 1000); Items(tick, items); Frame(); Frame(); }

        // ------------------------------------------------------------------ 1 to 4. the caption

        [Test]
        public void OneNearbyActionIsNamedInTheCaption()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, MeditateState());
            Assert.That(LauncherCaption(), Is.EqualTo("Meditate · Stay A While"), "the caption names the action and its quest");
            Assert.That(Count, Is.EqualTo(1), "one counted action (Abandon is not counted)");
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "the launcher shows");
            Assert.That(Launcher.GetComponent<Button>().interactable, Is.True, "and can be pressed");
        }

        [Test]
        public void OnlyAbandonShowsNoCaptionAndNoLauncher()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, AbandonOnly());
            Assert.That(Launcher.gameObject.activeSelf, Is.False, "no launcher for Abandon alone");
            Assert.That(LauncherCaption(), Is.EqualTo(""), "no caption");
            Assert.That(Count, Is.EqualTo(0), "no counted action");
            Assert.That(Child(FullStage, "PlayerHUD/QuestRewards").gameObject.activeSelf, Is.True, "the box shows for the accepted row");
        }

        [Test]
        public void AnActionWithoutAQuestNameReadsItsLabelAlone()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, PickupOnly());
            Assert.That(LauncherCaption(), Is.EqualTo("Pick up WallBuster"), "action 9 has no row: its label alone");
            Deliver(11, Accepted(75, "parcel_run", "", Act(75, 3, "Take the parcel")));
            Assert.That(LauncherCaption(), Is.EqualTo("Take the parcel"), "a row with an empty name: the label alone");
        }

        [Test]
        public void TwoActionsStillReadTheCount()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, TwoRows());
            Assert.That(Count, Is.EqualTo(2), "set-up: Meditate and the parcel are counted");
            Assert.That(LauncherCaption(), Is.EqualTo("Nearby quest actions · 2"));
        }

        // ------------------------------------------------------------------ 5. the window

        [Test]
        public void TheWindowReadsTheLabelFirst()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, TwoRows(Act(73, 9, "Pick up WallBuster", 37)));
            Assert.That(Count, Is.EqualTo(3), "set-up: three counted actions");
            Assert.That(LauncherCaption(), Is.EqualTo("Nearby quest actions · 3"));
            Launcher.GetComponent<Button>().onClick.Invoke();
            var overlay = HudTestPaths.Find(_hud.transform, "MatchUI/NearbyQuestActionsOverlay");
            Assert.That(overlay.gameObject.activeSelf, Is.True, "the click opens the nearby-actions window");
            string[] expected = { "Meditate · Stay A While", "Take the parcel · Smuggler's Run", "Pick up WallBuster" };
            for (int i = 0; i < expected.Length; i++)
            {
                var button = Child(overlay, "NearbyQuestActions/Actions/Viewport/Content/Action" + i);
                Assert.That(button.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo(expected[i]), "window button " + i);
            }
        }

        // ------------------------------------------------------------------ 6, 7. a new view, a reset

        [Test]
        public void ANewViewWithoutTheActionClearsTheCaption()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, MeditateState());
            Assert.That(LauncherCaption(), Is.EqualTo("Meditate · Stay A While"), "set-up: the action is named");
            Deliver(11, AbandonOnly());
            Assert.That(LauncherCaption(), Is.EqualTo(""), "no counted action: no caption");
            Assert.That(Launcher.gameObject.activeSelf, Is.False, "and no launcher");
            Assert.That(Cached, Is.EqualTo(""), "the cached caption is empty");
        }

        [Test]
        public void AResetClearsTheCaption()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, MeditateState());
            Assert.That(LauncherCaption(), Is.EqualTo("Meditate · Stay A While"), "set-up: the action is named");
            Call(_hud, "ResetQuestRewardHud");   // no frame after it: the session still holds the view and would bind it again
            Assert.That(LauncherCaption(), Is.EqualTo(""), "the reset clears the caption label");
            Assert.That(Count, Is.EqualTo(0), "and the count");
            Assert.That(Cached, Is.EqualTo(""), "and the cached caption");

            Deliver(11, PickupOnly());
            Assert.That(LauncherCaption(), Is.EqualTo("Pick up WallBuster"), "set-up: the pickup is named");
            Call(_hud, "ResetQuestRewardHud");
            Assert.That(LauncherCaption(), Is.EqualTo(""), "the second reset clears it again");
            Assert.That(Count, Is.EqualTo(0), "count again");
            Assert.That(Cached, Is.EqualTo(""), "cache again");
        }

        // ------------------------------------------------------------------ 8. an action beats the offer

        [Test]
        public void AnActionStillBeatsTheOffer()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            var state = Quests(new[] { AcceptedRow(71, "stay_a_while", "Stay A While"), OfferRow(72) }, new[] { Abandon(71), Meditate() });
            Deliver(10, state);
            Assert.That((bool)GetF(_hud, "_offerPrompt"), Is.True, "set-up: the offer is in reach");
            Assert.That(LauncherCaption(), Is.EqualTo("Meditate · Stay A While"), "the action keeps the caption");
        }

        // ------------------------------------------------------------------ 9. no garbage

        [Test]
        public void ASteadyFrameWithOneNamedActionAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, MeditateState());
            Assert.That(LauncherCaption(), Is.EqualTo("Meditate · Stay A While"), "set-up: the action is named");
            var refresh = Refresh();
            AllocationMeter.AssertNone(refresh, 200, "200 refresh frames with one named quest action");
        }

        // ------------------------------------------------------------------ 10. the longest caption fits

        static bool Wraps(TMP_Text t) => t.textWrappingMode != TextWrappingModes.NoWrap;

        static Vector2 Needs(TMP_Text label, string text, float size)
        {
            bool auto = label.enableAutoSizing; float keep = label.fontSize;
            label.enableAutoSizing = false; label.fontSize = size;
            var need = label.GetPreferredValues(text, Wraps(label) ? label.rectTransform.rect.width : 0f, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            return need;
        }

        static QuestsState Longest() => Accepted(72, "whispers_in_the_brush", "Whispers in the Brush", Abandon(72),
            Act(72, 3, "Collect hidden token"));

        [TestCase(1f)]
        [TestCase(1.5f)]
        public void TheLongestCaptionFitsTheBoxAtBothTextSizes(float textScale)
        {
            BuildFullRig(); LayoutFull(N2);
            Assert.That(FullStage.Find("PlayerHUD/CombatActions").localScale.x, Is.EqualTo(.6f).Within(.001f), "set-up: N2 sits at the .6 floor");
            Snap(10, 1000); Items(10, Longest()); Frame();
            Snap(11, 1000); Items(11, PickupInReach(true), Longest()); Frame();
            Assert.That((bool)GetF(_hud, "_promptPair"), Is.True, "set-up: the launcher and the pickup pill share the lane");
            const string text = "Collect hidden token · Whispers in the Brush";
            Assert.That(LauncherCaption(), Is.EqualTo(text), "the caption names the action");
            var label = Child(Launcher, "Label").GetComponent<TMP_Text>();
            Assert.That(label.rectTransform.rect.width, Is.EqualTo(220f).Within(.5f), "the caption box keeps its local width");
            MythicUi.ApplyTextScale(_hud.transform, textScale);
            Assert.That(label.gameObject.activeInHierarchy, Is.True, "the label is active in the hierarchy, so its measure is true");
            var follow = label.GetComponent<MythicTextScale>();
            Assert.That(follow, Is.Not.Null, "the caption has a MythicTextScale");
            label.text = text; follow.Refit();
            Assert.That(label.overflowMode, Is.Not.EqualTo(TextOverflowModes.Ellipsis).And.Not.EqualTo(TextOverflowModes.Truncate), "never cut");
            float size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
            string shown = " (text scale " + textScale + ", size " + size.ToString("0.##") + ")";
            Assert.That(size, Is.GreaterThanOrEqualTo(follow.MinSize - .001f), "at least the minimum size" + shown);
            float width = label.rectTransform.rect.width;
            var need = Needs(label, text, size);
            Assert.That(need.x, Is.LessThanOrEqualTo(width * MythicTextScale.WidthShare + .001f),
                "'" + text + "' needs " + need.x.ToString("0.#") + " of " + width.ToString("0.#") + shown);
        }

        // ------------------------------------------------------------------ 11. the fallback

        [Test]
        public void AnEmptyLabelFallsBackToTheCount()
        {
            BuildFullRig(); LayoutFull(new Vector2(1280, 800));
            Deliver(10, Accepted(75, "parcel_run", "", Act(75, 3, "")));
            Assert.That(Count, Is.EqualTo(1), "set-up: one counted action");
            Assert.That(LauncherCaption(), Is.EqualTo(CountOne), "an empty label and no name: the count text");
            Deliver(11, Accepted(75, "parcel_run", "Smuggler's Run", Act(75, 3, "")));
            Assert.That(Count, Is.EqualTo(1), "set-up: still one counted action");
            Assert.That(LauncherCaption(), Is.EqualTo(CountOne), "an empty label with a name: the launcher never shows an empty bar");
        }
    }
}
