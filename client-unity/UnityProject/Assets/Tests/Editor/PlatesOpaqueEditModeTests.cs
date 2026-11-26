using System;
using System.Collections.Generic;
using System.Linq;
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
    /// P-UI-PLATES-OPAQUE-1: no world name reads through a standing text plate of the match HUD. Every such plate (vitals, champion
    /// panel, Wind chip, ping pill, phase pill, banner, both toast lanes, quest tracker, offer card, terrain chips and note, hunt card,
    /// spectate bar, chest panel, tutorial card, inspector card) is drawn with alpha 1 whenever it is shown and settled, in the shipped
    /// theme and in a recoloured one whose surface tokens are translucent (.24). The HUD still ignores a theme change for its plates.
    /// One whole MatchHud on a real session over a fake transport (the TilePlatesEditModeTests rig). Nothing reads a clock.
    /// </summary>
    public sealed class PlatesOpaqueEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Hud = "MatchUI/HudStage/PlayerHUD/";

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

        GameObject _fullRoot, _events, _host;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        UiTheme _savedTheme;
        bool? _savedReduced;
        float? _savedNoticeDt;
        FactionId _faction = FactionId.Fire;
        uint _tick = 10;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _savedReduced = UiMotion.ReducedOverride;
            _savedNoticeDt = MatchHud.NoticeDtOverride;
            _priorEvents = EventSystem.current;
            UiMotion.ReducedOverride = true;     // every HudFade and toast snaps
            MatchHud.NoticeDtOverride = .05f;
        }

        [TearDown]
        public void TearDown()
        {
            HudIconHint.CloseAll();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            UiMotion.ReducedOverride = _savedReduced;
            MatchHud.NoticeDtOverride = _savedNoticeDt;
            if (_priorEvents != null) EventSystem.current = _priorEvents;
        }

        // ------------------------------------------------------------------ the theme and the full rig

        /// <summary>Assigned BEFORE the rig is built: the shipped placeholder, or a recoloured skin whose surfaces are translucent.</summary>
        static void UseTheme(bool recoloured)
        {
            var theme = UiTheme.Placeholder();
            if (recoloured)
            {
                theme = UiTheme.Skin();   // a fresh object
                theme.Surface = new Color32(90, 30, 30, 60);
                theme.SurfaceRaised = new Color32(30, 90, 30, 60);
                theme.SurfaceSunken = new Color32(30, 30, 90, 60);
                theme.OutlineDark = new Color32(10, 10, 60, 60);
            }
            UiTheme.Current = theme;
        }

        void BuildFullRig(FactionId faction, bool recoloured)
        {
            Assert.That(UiTheme.Current.Surface.a, Is.EqualTo(recoloured ? 60 : 240), "set-up: the theme assigned before the build is the one in force");
            _faction = faction;
            _priorEvents = EventSystem.current;
            _events = new GameObject("Plates opaque events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Plates opaque fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://plates-opaque/ws", Profile = "plates-opaque", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _hud.HudLocalMs = () => 5000d;   // EditMode time does not advance: Frame reads this seam
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(1, 50);
            Assert.That(UiTheme.Current.Surface.a, Is.EqualTo(recoloured ? 60 : 240), "set-up: the theme survived the build");
            Layout(new Vector2(1280, 800));
            Call(_hud, "SetPlateMode", true);
        }

        void Snap(uint tick, double serverMs, byte phase = (byte)MatchPhase.Active) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = phase,
                Me = new WireSelf { Id = 1, Faction = (byte)_faction, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        void Items(uint tick, params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = tick, Items = items });

        /// <summary>One frame of the HUD. A real match refreshes the hidden HUD first: every rig test calls this once before it delivers anything.</summary>
        void Frame() { Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        // ------------------------------------------------------------------ helpers

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

        /// <summary>Exact path under the HUD root, never a search by the last segment.</summary>
        Image PlateAt(string path)
        {
            var t = _hud.transform.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under the HUD");
            var image = t.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, path + " has an Image");
            return image;
        }

        /// <summary>The alpha the plate is drawn at: Image.color.a, times a ColorTint Button's normal colour alpha and colorMultiplier on the
        /// same object, times every CanvasGroup alpha up to the canvas (stopping after a group that ignores its parents).</summary>
        static float DrawnAlpha(Graphic g)
        {
            float alpha = g.color.a;
            var button = g.GetComponent<Button>();
            if (button != null && button.transition == Selectable.Transition.ColorTint)
                alpha *= button.colors.normalColor.a * button.colors.colorMultiplier;
            for (var t = g.transform; t != null; t = t.parent)
            {
                bool stop = false;
                foreach (var group in t.GetComponents<CanvasGroup>()) { alpha *= group.alpha; if (group.ignoreParentGroups) stop = true; }
                if (stop) break;
            }
            return alpha;
        }

        /// <summary>For a shown surface: it exists, is enabled and active in the hierarchy, and is drawn opaque. .999, not .99: .99 still leaves a 1.15:1 ghost.</summary>
        static void AssertOpaque(Graphic g, string surface)
        {
            Assert.That(g, Is.Not.Null, "set-up: " + surface + " exists");
            Assert.That(g.enabled, Is.True, "set-up: " + surface + " is shown");
            Assert.That(g.gameObject.activeInHierarchy, Is.True, "set-up: " + surface + " is shown");
            Assert.That(DrawnAlpha(g), Is.GreaterThanOrEqualTo(.999f), surface + " is see-through: drawn alpha " + DrawnAlpha(g));
        }

        /// <summary>For a pooled graphic that is not shown: the same drawn-alpha line, nothing about being active.</summary>
        static void AssertDrawnOpaque(Graphic g, string surface)
        {
            Assert.That(g, Is.Not.Null, "set-up: " + surface + " exists");
            Assert.That(g.enabled, Is.True, "set-up: " + surface + " is enabled");
            Assert.That(DrawnAlpha(g), Is.GreaterThanOrEqualTo(.999f), surface + " is see-through: drawn alpha " + DrawnAlpha(g));
        }

        // ------------------------------------------------------------------ 1. the plate, the champion panel, the Wind chip, the ping pill

        [TestCase(false)]
        [TestCase(true)]
        public void ThePlateTheChampionPanelTheWindChipAndThePingPillAreOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            AssertOpaque(PlateAt(Hud + "VitalsPanel"), "the vitals plate");
            AssertOpaque(PlateAt(Hud + "ChampionCombatHud"), "the champion panel");
            AssertOpaque(PlateAt(Hud + "CombatActions/Consumables/Wind"), "the Wind chip");
            // TickMetrics shows the pill, but it reads Time.realtimeSinceStartupAsDouble: show it by hand.
            ((RectTransform)GetF(_hud, "_metricsPill")).gameObject.SetActive(true);
            AssertOpaque(PlateAt("MatchUI/ChromeStage/PerformancePill"), "the ping pill");
        }

        // ------------------------------------------------------------------ 2. the pills at the top centre

        [TestCase(false)]
        [TestCase(true)]
        public void TheTopCentrePillsAreOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            _hud.ApplyPhaseStrip("Coliseum 9:30", Roguehalla.Net.Ui.Indicators.ArenaLineTone.Normal);
            Call(_hud, "SyncTopCentre");
            AssertOpaque(PlateAt("MatchUI/HudStage/PhaseTimerPill"), "the phase pill");

            // The banner last: a dropped connection hides the world HUD. No Frame() between the calls.
            _transport.Close();
            Assert.That(_session.State == SessionState.Reconnecting || _session.State == SessionState.Failed, Is.True,
                "set-up: the session dropped (" + _session.State + ")");
            Call(_hud, "SyncTopCentre");
            AssertOpaque(PlateAt("MatchUI/HudStage/ConnectionBanner"), "the connection banner");
        }

        // ------------------------------------------------------------------ 3. the toast pills of both lanes

        RectTransform Lane(string name)
        {
            var t = ((RectTransform)GetF(_hud, "_stage")).Find(name);
            Assert.That(t, Is.Not.Null, name + " exists under the HUD stage");
            return (RectTransform)t;
        }

        List<RectTransform> ActivePills(string lane)
        {
            var list = new List<RectTransform>();
            foreach (Transform child in Lane(lane))
                if (child.name.StartsWith("Toast") && child.gameObject.activeInHierarchy) list.Add((RectTransform)child);
            return list;
        }

        static TMP_Text TextOf(RectTransform pill)
        {
            var t = pill.Find("Text");
            Assert.That(t, Is.Not.Null, pill.name + " has a child 'Text'");
            return t.GetComponent<TMP_Text>();
        }

        RectTransform ActiveWithText(string lane, string text) => ActivePills(lane).FirstOrDefault(p => TextOf(p).text == text);

        [TestCase(false)]
        [TestCase(true)]
        public void TheToastPillsOfBothLanesAreOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            // The first-run hint is armed by Bind (no profile is owed it): its pill stands in FeedbackLane from the first frames, beside the refusal.
            _transport.Deliver(new ServerMsg.Events { Tick = _tick++, List = new List<WireEvent> {
                new WireEvent { Kind = EventKind.Toast, ToastText = "Slow down" },
                new WireEvent { Kind = EventKind.PickupRefused, PickupReason = 3 } } });
            Frame(); Frame(); Frame();
            Assert.That(ActiveWithText("EventLane", "Slow down"), Is.Not.Null, "set-up: EventLane shows 'Slow down'");
            Assert.That(ActiveWithText("FeedbackLane", "Can't pick that up"), Is.Not.Null, "set-up: FeedbackLane shows the refusal");

            foreach (string lane in new[] { "EventLane", "FeedbackLane" })
                foreach (var pill in ActivePills(lane))
                    AssertOpaque(pill.GetComponent<Image>(), lane + "/" + pill.name + " (shown)");
            foreach (string lane in new[] { "EventLane", "FeedbackLane" })
            {
                int seen = 0;
                foreach (Transform child in Lane(lane))
                {
                    if (!child.name.StartsWith("Toast")) continue;
                    seen++;
                    AssertDrawnOpaque(child.GetComponent<Image>(), lane + "/" + child.name + " (pooled)");
                }
                Assert.That(seen, Is.GreaterThan(0), "set-up: " + lane + " has pooled pills");
            }
        }

        // ------------------------------------------------------------------ 4. the quest tracker and the offer card

        static WireQuestRow Offer(uint handle) => new WireQuestRow
        {
            Handle = new QuestHandle(handle), Def = "fixture", GiverX = 70, GiverY = 64, State = QuestRowState.Preview, ExpiresAt = 10000,
            CanAccept = true, Name = "Clear the Barrow", GiverLabel = "Old Marta", Brief = "brief", Objective = "Bring back three lanterns", Reward = "Hook",
        };

        static WireQuestRow Running(uint handle) => new WireQuestRow
        {
            Handle = new QuestHandle(handle), Def = "hold_the_fort", GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = 61000,
            OwnerAccepted = true, OwnerProgress = 12, Name = "Hold the Fort", GiverLabel = "Captain", Brief = "brief",
            Objective = "Stay inside the ring", Reward = "Tornado",
        };

        [TestCase(false)]
        [TestCase(true)]
        public void TheQuestTrackerAndTheOfferCardAreOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            Items(_tick++, new QuestsState { Available = true, View = new WireQuestView { Rows = new[] { Offer(71), Running(72) } } });
            Frame(); Frame();
            Assert.That(Call(_hud, "OpenOffer"), Is.EqualTo(true), "set-up: the offer card opens");
            Frame();
            var tracker = (QuestTrackerView)GetF(_hud, "_questTracker");
            Assert.That(tracker.Rows, Is.GreaterThanOrEqualTo(1), "set-up: the tracker lists the running quest");
            AssertOpaque(PlateAt(Hud + "QuestTracker"), "the quest tracker");
            AssertOpaque(PlateAt(Hud + "QuestOffer"), "the quest offer card");
        }

        // ------------------------------------------------------------------ 5. the terrain chip and the home-ground note

        [TestCase(false)]
        [TestCase(true)]
        public void TheTerrainChipAndTheHomeGroundNoteAreOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            _hud.ShowTerrain(true, 6, MapBlob.GroundTerrainMap, FactionId.Holy);
            AssertOpaque(PlateAt(Hud + "TerrainChips/Chip0"), "the ice chip");
            // No frame or TickTerrain after: it hides the row again.
            _hud.ShowTerrain(true, 6, MapBlob.GroundTerrainMap, FactionId.Frost);
            AssertOpaque(PlateAt(Hud + "TerrainChips/Note"), "the home-ground note");
        }

        // ------------------------------------------------------------------ 6. the hunt card

        [TestCase(false)]
        [TestCase(true)]
        public void TheHuntCardIsOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Sentenced, recoloured);
            Frame(); Frame();
            Assert.That(_hud.HuntPanel.IsVisible, Is.True, "set-up: the hunt card is shown to a Sentenced player");
            AssertOpaque(PlateAt(Hud + "SentencedHunt"), "the hunt card");
        }

        // ------------------------------------------------------------------ 7. the spectate bar

        [TestCase(false)]
        [TestCase(true)]
        public void TheSpectateBarIsOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            _client.Spectator.Start(_session, 0u);
            Frame();
            AssertOpaque(PlateAt("MatchUI/HudStage/SpectateBar"), "the spectate bar");
        }

        // ------------------------------------------------------------------ 8. the chest panel

        [TestCase(false)]
        [TestCase(true)]
        public void TheChestPanelIsOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            Items(_tick++, new RewardChestsState { Rows = new[] { new WireRewardChest
            {
                Handle = new RewardChestHandle(73), X = 64, Y = 64, ExpiresAt = 15000, CanChoose = true,
                Options = new[]
                {
                    new WireRewardOption { Kind = 1, Id = 0 },
                    new WireRewardOption { Kind = 1, Id = 79 },
                    new WireRewardOption { Kind = 2, Id = 7 },
                },
            } } });
            Snap(_tick++, 100);
            Frame();
            AssertOpaque(PlateAt("MatchUI/HudStage/Rewards"), "the chest reward panel");
        }

        // ------------------------------------------------------------------ 9. the tutorial step card

        [TestCase(false)]
        [TestCase(true)]
        public void TheTutorialCardIsOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            BuildFullRig(FactionId.Fire, recoloured);
            Frame();
            var view = _hud.GetComponent<TutorialView>();
            Assert.That(view, Is.Not.Null, "set-up: the HUD carries a TutorialView");
            view.SetCard(true, "Move");   // no Frame() after: TickTutorial hides it
            AssertOpaque(PlateAt("MatchUI/HudStage/TutorialCard"), "the tutorial step card");
        }

        // ------------------------------------------------------------------ 10. the inspector card (own canvas, no rig)

        static InterpActor Row(uint id) => new InterpActor
        {
            Id = id, Kind = ActorKind.Player, Status = ActorStatus.Alive, Faction = FactionId.Frost, Weapon = WeaponId.Axe, HpPct = 60, TopStat1 = StatKey.Ad,
        };

        [TestCase(false)]
        [TestCase(true)]
        public void TheInspectorCardIsOpaque(bool recoloured)
        {
            UseTheme(recoloured);
            _host = new GameObject("Plates opaque inspector host", typeof(RectTransform));
            var view = new InspectorCardView(_host.transform);
            view.Show(new List<InterpActor> { Row(7) }, 7, 0, 1);
            Assert.That(view.Visible, Is.True, "set-up: the card is shown");
            AssertOpaque(view.Root.GetComponent<Image>(), "the inspector card");
        }

        // ------------------------------------------------------------------ standing guard: the HUD ignores a theme change for its plates

        [Test]
        public void TheHudIgnoresAThemeChangeForItsPlates()
        {
            UseTheme(false);
            BuildFullRig(FactionId.Fire, false);
            Frame();
            var paths = new[] { Hud + "VitalsPanel", Hud + "ChampionCombatHud", Hud + "CombatActions/Consumables/Wind" };
            var before = paths.Select(p => DrawnAlpha(PlateAt(p))).ToArray();
            float chestBefore = PlateAt("MatchUI/HudStage/Rewards").color.a;   // inactive: the colour only

            UseTheme(true);   // raises UiTheme.Changed
            Frame();
            Assert.That(UiTheme.Current.Surface.a, Is.EqualTo(60), "set-up: the recoloured skin is in force");
            for (int i = 0; i < paths.Length; i++)
                Assert.That(DrawnAlpha(PlateAt(paths[i])), Is.EqualTo(before[i]).Within(.001f), paths[i] + " kept its drawn alpha");
            Assert.That(PlateAt("MatchUI/HudStage/Rewards").color.a, Is.EqualTo(chestBefore).Within(.001f), "the chest panel kept its colour alpha");
        }
    }
}
