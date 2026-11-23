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

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PAIR-LANE-NARROW-1: the prompt lane scales with the action row. On a narrow stage the row shrinks and the pickup pill (a child of
    /// the row) shrinks with it; the quest launcher box (a child of the unscaled HUD) is scaled by the same factor and placed from
    /// HudRegions.PromptPair(index, scale) / HudRegions.PromptLone(scale), so the pair stays adjacent, centred and level, and a lone prompt
    /// does not hop when a second one appears. The rig is the full rig of AbilityRowEditModeTests: a whole MatchHud on a real MatchClient over
    /// a fake transport. Nothing here reads a clock and no pickup cue is sent (no press pulse plays).
    /// </summary>
    public sealed class PromptPairNarrowEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float Eps = .01f;
        static readonly Vector2 W = new Vector2(1280, 800);
        static readonly Vector2 N1 = new Vector2(700, 600);
        static readonly Vector2 N2 = new Vector2(640, 600);
        static readonly Vector2[] Narrow = { N1, N2 };

        // ------------------------------------------------------------------ the full rig (copied from AbilityRowEditModeTests)

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
            _events = new GameObject("Prompt pair events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Prompt pair fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://pair/ws", Profile = "pair", Now = () => 1000 });
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

        [TearDown]
        public void TearDown()
        {
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------ helpers

        struct Rect4 { public float Left, Top, Right, Bottom, Width, Height; }

        /// <summary>Insets (left, top, right, bottom) and size of a rect inside the stage, in HUD units.</summary>
        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Right = w / 2f - max.x, Top = h / 2f - max.y, Bottom = min.y + h / 2f, Width = max.x - min.x, Height = max.y - min.y };
        }

        static void AssertRect(Rect4 m, HudStageRect expected, string what)
        {
            Assert.That(m.Left, Is.EqualTo(expected.Left).Within(Eps), what + " left");
            Assert.That(m.Top, Is.EqualTo(expected.Top).Within(Eps), what + " top");
            Assert.That(m.Width, Is.EqualTo(expected.Width).Within(Eps), what + " width");
            Assert.That(m.Height, Is.EqualTo(expected.Height).Within(Eps), what + " height");
        }

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

        RectTransform FullStage => (RectTransform)GetF(_hud, "_stage");
        RectTransform FullBar => Child(FullStage, "PlayerHUD/CombatActions");
        RectTransform Box => Child(FullStage, "PlayerHUD/QuestRewards");
        RectTransform Launcher => Child(Box, "QuestActionLauncher");
        RectTransform Pill => Child(FullBar, "Pickup");
        bool PairOn => (bool)GetF(_hud, "_promptPair");

        static object GetF(object o, string name)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            return f.GetValue(o);
        }
        static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, All);
            Assert.That(m, Is.Not.Null, "missing method " + name);
            return m.Invoke(o, args.Length == 0 ? null : args);
        }

        /// <summary>The bar's scale, after asserting the premise of the window: 1 for W, .65 to .75 for N1, .6 for N2.</summary>
        float PremiseScale(Vector2 window)
        {
            float s = FullBar.localScale.x;
            if (window == W) Assert.That(s, Is.EqualTo(1f).Within(.001f), "set-up: the wide stage keeps the row at scale 1");
            else if (window == N1) Assert.That(s, Is.InRange(.65f, .75f), "set-up: N1 scales the row between .65 and .75 (it is " + s + ")");
            else Assert.That(s, Is.EqualTo(.6f).Within(.001f), "set-up: N2 sits at the .6 floor (it is " + s + ")");
            return s;
        }

        void DeliverPair()
        {
            Snap(10, 100); Items(10, NearbyAction()); Frame();
            Snap(11, 150); Items(11, PickupInReach(true), NearbyAction()); Frame();   // Props (key 2) before Quests (key 10)
        }

        HudStageRect OnStage(HudRegion region) => region.On(FullStage.rect.width, FullStage.rect.height);

        static string Name(Vector2 window) => window == W ? "W" : window == N1 ? "N1" : "N2";

        // ------------------------------------------------------------------ 6. the pair is one layout

        [TestCaseSource(nameof(Narrow))]
        public void ThePairIsOneLayoutOnANarrowStage(Vector2 window)
        {
            BuildFullRig(); LayoutFull(window);
            float s = PremiseScale(window);
            DeliverPair();
            var stage = FullStage; var pill = Pill; var box = Box;
            Assert.That(pill.gameObject.activeInHierarchy, Is.True, "set-up: the pickup pill shows");
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: the launcher shows");
            Assert.That(PairOn, Is.True, "set-up: they share the lane");
            AssertRect(Measure(stage, pill), OnStage(HudRegions.PromptPair(0, s)), "pill");
            AssertRect(Measure(stage, box), OnStage(HudRegions.PromptPair(1, s)), "box");
            var p = Measure(stage, pill); var b = Measure(stage, box);
            Assert.That(p.Left + p.Width, Is.LessThanOrEqualTo(b.Left + Eps), "the pill ends before the box starts");
            Assert.That(b.Left - (p.Left + p.Width), Is.EqualTo(8f * s).Within(Eps), "the gap is 8 s");
            Assert.That(b.Top, Is.EqualTo(p.Top).Within(Eps), "equal top");
            Assert.That(b.Bottom, Is.EqualTo(p.Bottom).Within(Eps), "equal bottom");
            Assert.That(b.Height, Is.EqualTo(p.Height).Within(Eps), "equal height");
            Assert.That(p.Left, Is.EqualTo(b.Right).Within(Eps), "the pair is centred (pill left inset equals box right inset)");
            foreach (var r in new[] { p, b })
            {
                Assert.That(r.Left, Is.GreaterThanOrEqualTo(-Eps), "inside the stage: left");
                Assert.That(r.Right, Is.GreaterThanOrEqualTo(-Eps), "inside the stage: right");
                Assert.That(r.Top, Is.GreaterThanOrEqualTo(-Eps), "inside the stage: top");
                Assert.That(r.Bottom, Is.GreaterThanOrEqualTo(-Eps), "inside the stage: bottom");
            }
            Assert.That(p.Width / 220f, Is.EqualTo(s).Within(Eps / 10f), "pill width over 220 is s");
            Assert.That(b.Width / 220f, Is.EqualTo(s).Within(Eps / 10f), "box width over 220 is s");
        }

        // ------------------------------------------------------------------ 7. scaled, not resized

        [Test]
        public void TheLauncherBoxIsScaledNotResized()
        {
            BuildFullRig(); LayoutFull(N1);
            float s = PremiseScale(N1);
            DeliverPair();
            Assert.That(PairOn, Is.True, "set-up: they share the lane");
            var box = Box; var pill = Pill;
            Assert.That(box.sizeDelta.x, Is.EqualTo(220f).Within(Eps), "box natural width");
            Assert.That(box.sizeDelta.y, Is.EqualTo(26f).Within(Eps), "box natural height");
            Assert.That(box.localScale.x, Is.EqualTo(s).Within(.001f), "box scale x");
            Assert.That(box.localScale.y, Is.EqualTo(s).Within(.001f), "box scale y");
            Assert.That(box.localScale.z, Is.EqualTo(1f).Within(.001f), "box scale z");
            Assert.That(pill.sizeDelta.x, Is.EqualTo(220f).Within(Eps), "pill natural width");
            Assert.That(pill.sizeDelta.y, Is.EqualTo(26f).Within(Eps), "pill natural height");
            Assert.That(pill.localScale, Is.EqualTo(Vector3.one), "the pill has no scale of its own: the row scales it");
        }

        // ------------------------------------------------------------------ 8. the lane follows the row

        [TestCase(true)]
        [TestCase(false)]
        public void TheLaneFollowsTheRowWhenTheStageChanges(bool pair)
        {
            BuildFullRig(); LayoutFull(W);
            PremiseScale(W);
            if (pair) DeliverPair();
            else { Snap(10, 100); Items(10, NearbyAction()); Frame(); }
            Assert.That(PairOn, Is.EqualTo(pair), "set-up: the pair state");
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: the launcher shows");

            foreach (var window in Narrow)
            {
                LayoutFull(window); Frame();
                float s = PremiseScale(window);
                var stage = FullStage;
                string at = (pair ? "pair at " : "alone at ") + Name(window);
                AssertRect(Measure(stage, Box), OnStage(pair ? HudRegions.PromptPair(1, s) : HudRegions.PromptLone(s)), "box " + at);
                if (pair) AssertRect(Measure(stage, Pill), OnStage(HudRegions.PromptPair(0, s)), "pill " + at);
            }
            Assert.That(FullBar.localScale.x, Is.EqualTo(.6f).Within(.001f), "set-up: the last narrow window is the .6 floor");

            LayoutFull(W); Frame();
            PremiseScale(W);
            Assert.That(Box.localScale.x, Is.EqualTo(1f).Within(.001f), "back wide: box scale x is one");
            Assert.That(Box.localScale.y, Is.EqualTo(1f).Within(.001f), "back wide: box scale y is one");
            AssertRect(Measure(FullStage, Box), OnStage(pair ? HudRegions.PromptPair(1) : HudRegions.Prompt()), "box back wide");
        }

        // ------------------------------------------------------------------ 9. wide guard

        [Test]
        public void TheWideLanesAreUnchanged()
        {
            BuildFullRig(); LayoutFull(W);
            PremiseScale(W);
            var stage = FullStage; var box = Box; var pill = Pill;

            Snap(10, 100); Items(10, NearbyAction()); Frame();
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: launcher alone");
            AssertRect(Measure(stage, box), OnStage(HudRegions.Prompt()), "launcher alone");
            Assert.That(box.localScale, Is.EqualTo(Vector3.one), "launcher alone: scale one");

            Snap(11, 150); Items(11, PickupInReach(true), NearbyAction()); Frame();
            Assert.That(PairOn, Is.True, "set-up: pair");
            AssertRect(Measure(stage, pill), OnStage(HudRegions.PromptPair(0)), "pill in the pair");
            AssertRect(Measure(stage, box), OnStage(HudRegions.PromptPair(1)), "box in the pair");
            Assert.That(box.localScale, Is.EqualTo(Vector3.one), "pair: scale one");

            Snap(12, 200); Items(12, PickupInReach(true), Quests()); Frame();
            Assert.That(box.gameObject.activeSelf, Is.False, "set-up: an empty quest view hides the launcher");
            Assert.That(pill.gameObject.activeInHierarchy, Is.True, "set-up: pill alone");
            AssertRect(Measure(stage, pill), OnStage(HudRegions.Prompt()), "pill alone");

            Snap(13, 250); Items(13, PickupInReach(true), NearbyAction()); Frame();
            Assert.That(PairOn, Is.True, "set-up: pair again");
            AssertRect(Measure(stage, pill), OnStage(HudRegions.PromptPair(0)), "pill in the pair again");
            AssertRect(Measure(stage, box), OnStage(HudRegions.PromptPair(1)), "box in the pair again");
            Assert.That(box.localScale, Is.EqualTo(Vector3.one), "pair again: scale one");
        }

        // ------------------------------------------------------------------ 10. a lone prompt sits in the scaled lane

        [TestCaseSource(nameof(Narrow))]
        public void ALonePromptSitsInTheScaledLane(Vector2 window)
        {
            BuildFullRig(); LayoutFull(window);
            float s = PremiseScale(window);
            var stage = FullStage; var box = Box; var pill = Pill;
            Vector3 scaled = new Vector3(s, s, 1f);

            Snap(10, 100); Items(10, NearbyAction()); Frame();
            Assert.That(Launcher.gameObject.activeInHierarchy, Is.True, "set-up: launcher alone");
            Assert.That(PairOn, Is.False, "launcher alone is not a pair");
            AssertRect(Measure(stage, box), OnStage(HudRegions.PromptLone(s)), "launcher alone");
            Assert.That(box.localScale.x, Is.EqualTo(s).Within(.001f), "alone: scale x");
            Assert.That(box.localScale.y, Is.EqualTo(s).Within(.001f), "alone: scale y");
            Assert.That(box.localScale.z, Is.EqualTo(1f).Within(.001f), "alone: scale z");
            var alone = Measure(stage, box);

            Snap(11, 150); Items(11, PickupInReach(true), NearbyAction()); Frame();
            Assert.That(PairOn, Is.True, "set-up: the pickup joins");
            var paired = Measure(stage, box);
            Assert.That(paired.Top, Is.EqualTo(alone.Top).Within(Eps), "the box does not hop: top");
            Assert.That(paired.Bottom, Is.EqualTo(alone.Bottom).Within(Eps), "the box does not hop: bottom");
            Assert.That(paired.Height, Is.EqualTo(alone.Height).Within(Eps), "the box does not resize: height");
            Assert.That(box.localScale.x, Is.EqualTo(s).Within(.001f), "paired: scale x unchanged");
            Assert.That(box.localScale.y, Is.EqualTo(s).Within(.001f), "paired: scale y unchanged");

            Snap(12, 200); Items(12, PickupInReach(false), NearbyAction()); Frame();
            Assert.That(pill.gameObject.activeInHierarchy, Is.False, "set-up: the pickup is gone");
            Assert.That(PairOn, Is.False, "the pair ended");
            AssertRect(Measure(stage, box), OnStage(HudRegions.PromptLone(s)), "launcher alone again");
            Assert.That(box.localScale.x, Is.EqualTo(s).Within(.001f), "alone again: scale x");
            Assert.That(box.localScale.y, Is.EqualTo(s).Within(.001f), "alone again: scale y");

            // an empty but available quest view hides the launcher (a later Items keeps the earlier Quests item, so the view is replaced)
            Snap(13, 250); Items(13, PickupInReach(true), Quests()); Frame();
            Assert.That(box.gameObject.activeSelf, Is.False, "the launcher is hidden");
            Assert.That(pill.gameObject.activeInHierarchy, Is.True, "set-up: the pill is alone");
            var lone = OnStage(HudRegions.PromptLone(s));
            AssertRect(Measure(stage, pill), lone, "pill alone");
            Assert.That(Measure(stage, pill).Left, Is.EqualTo(stage.rect.width / 2f - 150f * s).Within(Eps), "pill alone: left");
            Assert.That(Measure(stage, pill).Width, Is.EqualTo(300f * s).Within(Eps), "pill alone: width");
            Assert.That(Measure(stage, pill).Height, Is.EqualTo(26f * s).Within(Eps), "pill alone: height");
            Assert.That(Measure(stage, pill).Bottom, Is.EqualTo(14f + 116f * s).Within(Eps), "pill alone: bottom edge");

            Snap(14, 300); Items(14, PickupInReach(true), NearbyAction()); Frame();
            Assert.That(PairOn, Is.True, "set-up: the pair again");
            Call(_hud, "ResetQuestRewardHud");   // no Frame() after it: the session still holds the view and the pickup, which would show the pair again
            Assert.That(box.gameObject.activeSelf, Is.False, "reset: the box is hidden");
            Assert.That(PairOn, Is.False, "reset: the pair ended");
            Assert.That(box.localScale.x, Is.EqualTo(scaled.x).Within(.001f), "reset: scale x");
            Assert.That(box.localScale.y, Is.EqualTo(scaled.y).Within(.001f), "reset: scale y");
            Assert.That(box.localScale.z, Is.EqualTo(1f).Within(.001f), "reset: scale z");
        }

        // ------------------------------------------------------------------ 11. a steady frame

        [Test]
        public void ASteadyPairFrameAllocatesNothingAndWritesNoRect()
        {
            BuildFullRig(); LayoutFull(N1);
            PremiseScale(N1);
            DeliverPair();
            Assert.That(PairOn, Is.True, "set-up: the pair");
            var refresh = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));
            for (int i = 0; i < 5; i++) refresh();   // warm up
            AllocationMeter.AssertNone(refresh, 200, "200 steady refresh frames beside the pickup pill at a narrow stage");
            var box = Box;
            box.anchoredPosition = new Vector2(999f, 5f);
            box.localScale = new Vector3(2f, 2f, 1f);
            refresh(); refresh(); refresh();
            Assert.That(box.anchoredPosition, Is.EqualTo(new Vector2(999f, 5f)), "a steady frame writes no position");
            Assert.That(box.localScale, Is.EqualTo(new Vector3(2f, 2f, 1f)), "a steady frame writes no scale");
        }

        // ------------------------------------------------------------------ 12. the labels still fit

        static bool Wraps(TMP_Text t) => t.textWrappingMode != TextWrappingModes.NoWrap;

        static Vector2 Needs(TMP_Text label, string text, float size)
        {
            bool auto = label.enableAutoSizing; float keep = label.fontSize;
            label.enableAutoSizing = false; label.fontSize = size;
            var need = label.GetPreferredValues(text, Wraps(label) ? label.rectTransform.rect.width : 0f, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            return need;
        }

        [TestCase(1f)]
        [TestCase(1.5f)]
        public void TheLabelsStillFitAtTheSmallScale(float textScale)
        {
            BuildFullRig(); LayoutFull(N2);
            PremiseScale(N2);
            DeliverPair();
            Assert.That(PairOn, Is.True, "set-up: the pair");
            MythicUi.ApplyTextScale(_hud.transform, textScale);
            var caption = Child(Launcher, "Label").GetComponent<TMP_Text>();
            var pillText = Child(Pill, "Label").GetComponent<TMP_Text>();
            Assert.That(caption.rectTransform.rect.width, Is.EqualTo(220f).Within(.5f), "the caption box keeps its local width");
            Assert.That(pillText.rectTransform.rect.width, Is.EqualTo(184f).Within(.5f), "the pill text box keeps its local width");
            var problems = new List<string>();
            void Check(string what, TMP_Text label, string text)
            {
                var follow = label.GetComponent<MythicTextScale>();
                if (follow == null) { problems.Add(what + " has no MythicTextScale"); return; }
                label.text = text; follow.Refit();
                if (label.overflowMode == TextOverflowModes.Ellipsis || label.overflowMode == TextOverflowModes.Truncate)
                    problems.Add(what + " would be cut (overflow " + label.overflowMode + ")");
                float size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
                if (size < follow.MinSize - .001f) problems.Add(what + " fits at " + size + ", under the minimum " + follow.MinSize);
                var need = Needs(label, text, size);
                float width = label.rectTransform.rect.width;
                if (need.x > width * MythicTextScale.WidthShare + .001f && size > follow.MinSize + .001f)
                    problems.Add(what + " '" + text + "' at " + size + " needs " + need.x.ToString("0.#") + " of " + width.ToString("0.#") + " and is above the minimum");
            }
            Check("launcher caption", caption, "Talk to Quartermaster Ilvane the Elder");
            Check("pill text", pillText, "F swap Shield for Wand");
            Assert.That(problems, Is.Empty, string.Join("\n", problems) + "\n(text scale " + textScale + ")");
        }
    }
}
