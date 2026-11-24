using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-LEFT-EDGE-1 oracle: the plate's GAINED table and the champion panel fold from their headers, the plate chain
    /// follows in the click frame, and the choice is stored by a click only. A whole HUD on a real MatchClient (the rig of
    /// QuestTrackerEditModeTests). EditMode never plays, so no rig reads the PlayerPrefs of the machine: folds are set through
    /// ApplyFolds or a click, and both keys are saved and restored around every test.</summary>
    public sealed class LeftEdgeFoldEditModeTests
    {
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

        static readonly Vector2[] Windows = { new Vector2(1280, 800), new Vector2(1920, 1080), new Vector2(2560, 1440) };
        const string P = "MatchUI/HudStage/PlayerHUD/";
        const string Gains = "VitalsPanel/PlateBody/BuildStrip/GainedLabel";
        const string GainsKey = UiDisplayPreferences.GainsFoldedKey, ChampionKey = UiDisplayPreferences.ChampionFoldedKey;
        static readonly int[] ThreeGains = { 240000, 0, 1900, 0, 0, 1500000, 0, 0, 0 };

        GameObject _root, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        bool? _priorReduced;
        uint _tick = 2;
        double _serverMs = 50;
        bool _hadGains, _hadChampion;
        int _oldGains, _oldChampion;
        float? _priorScale;
        readonly List<GameObject> _extra = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _priorReduced = UiMotion.ReducedOverride; UiMotion.ReducedOverride = true;
            _priorEvents = EventSystem.current;
            _hadGains = PlayerPrefs.HasKey(GainsKey); _oldGains = PlayerPrefs.GetInt(GainsKey, 0);
            _hadChampion = PlayerPrefs.HasKey(ChampionKey); _oldChampion = PlayerPrefs.GetInt(ChampionKey, 0);
            PlayerPrefs.DeleteKey(GainsKey); PlayerPrefs.DeleteKey(ChampionKey);
            BuildRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_priorScale.HasValue && _hud != null) SetScale(_priorScale.Value);
            DestroyRig();
            UiMotion.ReducedOverride = _priorReduced;
            if (_hadGains) PlayerPrefs.SetInt(GainsKey, _oldGains); else PlayerPrefs.DeleteKey(GainsKey);
            if (_hadChampion) PlayerPrefs.SetInt(ChampionKey, _oldChampion); else PlayerPrefs.DeleteKey(ChampionKey);
            PlayerPrefs.Save();
            if (_priorEvents != null) EventSystem.current = _priorEvents;
        }

        // The copied SetUp body of QuestTrackerEditModeTests, then one refresh so that the champion panel is shown: the snapshot is Active
        // with Status 1, Bind leaves the panel inactive and ExecuteHierarchy skips inactive objects.
        void BuildRig()
        {
            _tick = 2; _serverMs = 50;
            _events = new GameObject("Left edge events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("Left edge fixture");
            _client = _root.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://leftedge/ws", Profile = "leftedge", Now = () => 1000 });
            Invoke(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open(); Welcome(_transport);
            Snapshot(MatchPhase.Active, 50);
            Invoke(_hud, "SetPlateMode", true);
            Layout(Windows[0]);
            Invoke(_hud, "RefreshLegacyHud", _session.Me, _session.ServerTimeMs);
            Assert.IsTrue(((RectTransform)Field(_hud, "_championHud")).gameObject.activeInHierarchy, "rig: the champion panel is shown");
        }

        void DestroyRig()
        {
            foreach (var go in _extra) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _extra.Clear();
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            _root = null; _events = null; _hud = null;
        }

        static void Welcome(Transport t) => t.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16],
            Session = new byte[16], PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });

        void Snapshot(MatchPhase phase, double serverMs, byte faction = 0)
        {
            _serverMs = serverMs;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick++, ServerTimeOverrideMs = serverMs, Phase = (byte)phase,
                Me = new WireSelf { Id = 1, Faction = faction, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
        }

        static WireQuestRow Quest(uint handle, double expiresAt)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = "hold_the_fort", GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = expiresAt,
                Name = "Hold the Fort", GiverLabel = "Giver", Brief = "brief", Objective = "Stay inside the ring", Reward = "reward",
                OwnerAccepted = true, OwnerReady = false, OwnerProgress = 12,
                Participants = new[] { new WireQuestParticipant { Id = new RosterId(7), Accepted = true } },
            };
        }

        void DeliverQuest(params WireQuestRow[] rows) => _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new QuestsState { Available = true,
            View = new WireQuestView { OwnerRoster = new RosterId(7), Roster = new[] { new WireQuestRosterEntry { Id = new RosterId(7), Label = "owner" } },
            Rows = rows, Objectives = new WireQuestObjective[0] } } } });

        // The owner's build summary through the session, then the HUD's own refresh of the collections, as a frame does.
        WireBuildSummary DeliverSummary(params int[] gains)
        {
            var summary = new WireBuildSummary();
            var use = gains.Length > 0 ? gains : ThreeGains;
            for (int i = 0; i < use.Length; i++) summary.Gains[i] = use[i];
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new BuildSummaryState { Value = summary } } });
            Invoke(_hud, "RefreshBuildIcons", _session.Collections);
            return summary;
        }

        void Frame() { Invoke(_hud, "TickPlayerFlows"); Invoke(_hud, "TickTerrain"); Invoke(_hud, "RefreshReadouts"); }
        void Frames() { Frame(); Frame(); }

        // ---- finding, measuring, clicking

        Transform Find(string path)
        {
            var t = _hud.transform.Find(P + path);
            Assert.IsNotNull(t, "no such object: " + path);   // FIND-CHECK
            return t;
        }

        RectTransform Stage() { return (RectTransform)_hud.transform.Find("MatchUI/HudStage"); }
        RectTransform R(string field) { return (RectTransform)Field(_hud, field); }
        RectTransform Tracker() { return (RectTransform)HudTestPaths.Find(_hud.transform, "MatchUI/HudStage/PlayerHUD/QuestTracker"); }
        TMP_Text HeaderText() { return Find(Gains + "/Label").GetComponent<TMP_Text>(); }

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Field(_hud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Field(_hud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        struct Rect4 { public float Left, Top, Width, Height; public float Bottom => Top + Height; }

        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Top = h / 2f - max.y, Width = max.x - min.x, Height = max.y - min.y };
        }

        Rect4 M(RectTransform r) { return Measure(Stage(), r); }
        float PlateHeight() { return M(R("_vitalsPanel")).Height; }
        float ChampionHeight() { return M(R("_championHud")).Height; }
        float ChampionBottomGap() { return Stage().rect.height - M(R("_championHud")).Bottom; }

        float ChainBottom()
        {
            float bottom = M(R("_vitalsPanel")).Bottom;
            var terrain = R("_terrainRow");
            if (terrain != null && terrain.gameObject.activeSelf) bottom = Math.Max(bottom, M(terrain).Bottom);
            return bottom;
        }

        static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // A left click on the header: the click handler of its Button, found up the hierarchy. It must be the object itself.
        void Click(Transform header)
        {
            Assert.IsTrue(header.gameObject.activeInHierarchy, header.name + " is active in the hierarchy");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var handled = ExecuteEvents.ExecuteHierarchy<IPointerClickHandler>(header.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.AreSame(header.gameObject, handled, header.name + " handles the click itself");
        }

        void ClickGains() { Click(Find(Gains + "/HitBox")); }
        void ClickChampion() { Click(Find("ChampionCombatHud/HeadToggle")); }

        // The cells' own switch (activeSelf) or whether they show (activeInHierarchy: the strip must be showing too).
        void AssertCells(bool active, string why, bool ownSwitch)
        {
            var cells = (RectTransform[])Field(_hud, "_buildCells");
            Assert.AreEqual(9, cells.Length, "nine cells");
            for (int i = 0; i < cells.Length; i++) Assert.AreEqual(active, ownSwitch ? cells[i].gameObject.activeSelf : cells[i].gameObject.activeInHierarchy, "cell " + i + " " + why);
        }

        static void AssertSameRect(Rect4 a, Rect4 b, string what)
        {
            Assert.AreEqual(a.Left, b.Left, .01f, what + " left");
            Assert.AreEqual(a.Top, b.Top, .01f, what + " top");
            Assert.AreEqual(a.Width, b.Width, .01f, what + " width");
            Assert.AreEqual(a.Height, b.Height, .01f, what + " height");
        }

        // ---- data of ChampionPanelNumbersEditModeTests: the committed v17 owner panel

        static byte[] Server(string name)
        {
            var d = new DirectoryInfo(Application.dataPath);
            while (d != null && !File.Exists(System.IO.Path.Combine(d.FullName, "protocol", "fixtures", "v17.json"))) d = d.Parent;
            Assert.That(d, Is.Not.Null, "protocol/fixtures/v17.json above " + Application.dataPath);
            var m = Regex.Match(File.ReadAllText(System.IO.Path.Combine(d.FullName, "protocol", "fixtures", "v17.json")),
                "\"name\": \"" + Regex.Escape(name) + "\", \"hex\": \"([0-9a-f]+)\"");
            if (m.Success)
            {
                string hex = m.Groups[1].Value;
                return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            }
            throw new InvalidOperationException("no server vector " + name);
        }

        static WireBuildSummary PanelSummary() { return ((BuildSummaryState)((ServerMsg.State)ServerMsg.Decode(Server("state_build_summary_panel_tooltip"))).Items[0]).Value; }
        static SelfView Me(FactionId faction, WeaponId weapon) { return new SelfView { Faction = faction, Weapon = weapon, Status = ActorStatus.Alive, Hp = 100, MaxHp = 100 }; }

        // ---- tests

        [Test]
        public void TheDefaultIsUnfolded()
        {
            Assert.AreEqual(UiDisplayPreferences.GainsFoldedDefault, _hud.GainsFolded, "gains flag");
            Assert.AreEqual(UiDisplayPreferences.ChampionFoldedDefault, _hud.ChampionFolded, "champion flag");
            Assert.IsFalse(UiDisplayPreferences.GainsFoldedDefault, "the lane lead's ruling: open by default");
            Assert.IsFalse(UiDisplayPreferences.ChampionFoldedDefault, "the lane lead's ruling: open by default");
            Assert.AreEqual(148f, PlateHeight(), .01f, "plate");
            Assert.AreEqual(126f, ChampionHeight(), .01f, "champion");
            AssertCells(true, "is switched on", true);
            Assert.AreEqual("GAINED ▲", HeaderText().text, "header");
        }

        [Test]
        public void TheGainsHeaderFoldsTheTableAndTheTrackerRisesInTheSameFrame()
        {
            DeliverQuest(Quest(1, _serverMs + 134000));
            Frames();
            Assert.AreEqual(166f, M(Tracker()).Top, .6f, "set-up: the tracker under the open plate");
            DeliverSummary();
            float detailsBefore = M((RectTransform)Find("VitalsPanel/PlateBody/BuildDetails")).Top;
            ClickGains();
            Assert.IsTrue(_hud.GainsFolded, "flag");
            Assert.AreEqual(63f, PlateHeight(), .01f, "plate");
            Assert.IsTrue(_hud.GainedStripVisible, "the strip stays");
            AssertCells(false, "is hidden", false);
            Assert.AreEqual("GAINED · 3 ▼", HeaderText().text, "header");
            Assert.AreEqual(detailsBefore - 85f, M((RectTransform)Find("VitalsPanel/PlateBody/BuildDetails")).Top, .01f, "BuildDetails 85 higher");
            Assert.IsFalse(_hud.BlocksKeyboardInput, "a click on a header blocks no keyboard");
            Assert.IsTrue(R("_vitalsPanel").gameObject.activeSelf, "plate root active");
            Assert.IsTrue(R("_championHud").gameObject.activeSelf, "champion root active");
            Invoke(_hud, "TickPlayerFlows");
            Assert.AreEqual(81f, M(Tracker()).Top, .6f, "tracker top after one flows tick");
            Assert.AreEqual(ChainBottom() + 8f, M(Tracker()).Top, .6f, "tracker 8 under the chain");
        }

        [Test]
        public void AnotherClickRestoresTheFirstRectsExactly()
        {
            DeliverSummary();
            DeliverQuest(Quest(1, _serverMs + 134000));
            Frames();
            Rect4[] Take() => new[] { M(R("_vitalsPanel")), M((RectTransform)Find("VitalsPanel/PlateBody/BuildStrip")),
                M((RectTransform)Find("VitalsPanel/PlateBody/BuildDetails")), M(R("_championHud")), M(Tracker()), M((RectTransform)Find(Gains + "/HitBox")) };
            string[] names = { "plate", "BuildStrip", "BuildDetails", "champion", "tracker", "HitBox" };
            var first = Take();
            ClickGains(); ClickChampion();
            Frames();
            ClickGains(); ClickChampion();
            Frames();
            var again = Take();
            for (int i = 0; i < names.Length; i++) AssertSameRect(first[i], again[i], names[i]);
        }

        [Test]
        public void TheChampionHeaderFoldsTheBodyAndKeepsTheRoot()
        {
            var champion = R("_championHud");
            Assert.AreEqual(126f, ChampionHeight(), .01f, "height before");
            Assert.AreEqual(48f, ChampionBottomGap(), .01f, "bottom before");
            Assert.AreEqual(M(champion).Top - 8f, (float)Invoke(_hud, "TrackerFloor"), .01f, "floor before: the panel's top less 8");
            Assert.AreEqual(618f, (float)Invoke(_hud, "TrackerFloor"), .01f, "floor before");
            string name = ((TMP_Text)Field(_hud, "_championName")).text, weapon = ((TMP_Text)Field(_hud, "_damageWeapon")).text;
            var toggle = (RectTransform)Find("ChampionCombatHud/HeadToggle");
            var panel = M(champion); var head = M(toggle);
            Assert.AreEqual(panel.Left + 1f, head.Left, .01f, "left edge 1 inside");
            Assert.AreEqual(panel.Left + panel.Width - 1f, head.Left + head.Width, .01f, "right edge 1 inside");
            Assert.AreEqual(panel.Top + 1f, head.Top, .01f, "top edge 1 inside");
            Assert.AreEqual(26f, head.Height, .01f, "header height");
            ClickChampion();
            Assert.AreEqual(28f, ChampionHeight(), .01f, "height folded");
            Assert.AreEqual(48f, ChampionBottomGap(), .01f, "bottom folded");
            Assert.IsTrue(champion.gameObject.activeSelf, "the root stays active");
            Assert.IsFalse(Find("ChampionCombatHud/ChampionBody").gameObject.activeSelf, "the body is folded");
            Assert.IsTrue(((RectTransform[])Field(_hud, "_championRow"))[2].gameObject.activeSelf, "the rows keep their own state");
            var nameLabel = (TMP_Text)Field(_hud, "_championName"); var weaponLabel = (TMP_Text)Field(_hud, "_damageWeapon");
            Assert.IsTrue(nameLabel.gameObject.activeInHierarchy, "name shows");
            Assert.IsTrue(weaponLabel.gameObject.activeInHierarchy, "weapon shows");
            Assert.AreEqual(name, nameLabel.text, "name text");
            Assert.AreEqual(weapon, weaponLabel.text, "weapon text");
            Assert.AreEqual("▼", Find("ChampionCombatHud/HeadToggle/FoldGlyph/Label").GetComponent<TMP_Text>().text, "glyph folded");
            Assert.AreEqual(716f, (float)Invoke(_hud, "TrackerFloor"), .01f, "floor folded");
            panel = M(champion); head = M(toggle);
            Assert.AreEqual(panel.Left + 1f, head.Left, .01f, "folded: left edge 1 inside");
            Assert.AreEqual(panel.Left + panel.Width - 1f, head.Left + head.Width, .01f, "folded: right edge 1 inside");
            Assert.AreEqual(panel.Top + 1f, head.Top, .01f, "folded: top edge 1 inside");
            Assert.AreEqual(panel.Bottom - 1f, head.Bottom, .01f, "folded: 1 above the panel's bottom");
        }

        [Test]
        public void ARowRefreshAndASummaryDoNotUndoAFold()
        {
            Invoke(_hud, "ApplyFolds", true, true);
            var rows = (RectTransform[])Field(_hud, "_championRow");
            var me = Me(FactionId.Fire, WeaponId.Wand);
            Invoke(_hud, "ApplyChampionRows", null, me);
            AssertRowsFolded(rows, false, "no summary");
            Invoke(_hud, "ApplyChampionRows", PanelSummary(), me);
            AssertRowsFolded(rows, true, "the panel summary");
            DeliverSummary();
            Invoke(_hud, "RefreshLegacyHud", _session.Me, _session.ServerTimeMs);
            _hud.ResetTransientPresentation();
            Assert.AreEqual(63f, PlateHeight(), .01f, "plate");
            Assert.AreEqual("GAINED · 3 ▼", HeaderText().text, "the count follows");
            Assert.IsTrue(_hud.GainsFolded, "gains flag");
            Assert.IsTrue(_hud.ChampionFolded, "champion flag");
        }

        void AssertRowsFolded(RectTransform[] rows, bool data, string when)
        {
            Assert.AreEqual(data, rows[0].gameObject.activeSelf, when + ": row 0 follows the data");
            Assert.AreEqual(data, rows[1].gameObject.activeSelf, when + ": row 1 follows the data");
            Assert.IsFalse(rows[0].gameObject.activeInHierarchy, when + ": row 0 is hidden by the fold");
            Assert.IsFalse(rows[1].gameObject.activeInHierarchy, when + ": row 1 is hidden by the fold");
            Assert.IsFalse(Find("ChampionCombatHud/ChampionBody").gameObject.activeSelf, when + ": the body is folded");
            Assert.AreEqual(28f, ChampionHeight(), .01f, when + ": the panel is folded");
        }

        [Test]
        public void TheClickWritesTheKeyAndTheStoredReadersReadIt()
        {
            Assert.AreEqual(UiDisplayPreferences.GainsFoldedDefault, UiDisplayPreferences.GainsFoldedStored, "gains default");
            Assert.AreEqual(UiDisplayPreferences.ChampionFoldedDefault, UiDisplayPreferences.ChampionFoldedStored, "champion default");
            DeliverSummary();
            ClickGains(); ClickChampion();
            Assert.AreEqual(1, PlayerPrefs.GetInt(GainsKey, -1), "gains key after a click");
            Assert.AreEqual(1, PlayerPrefs.GetInt(ChampionKey, -1), "champion key after a click");
            Assert.IsTrue(UiDisplayPreferences.GainsFoldedStored, "gains stored");
            Assert.IsTrue(UiDisplayPreferences.ChampionFoldedStored, "champion stored");
            ClickGains(); ClickChampion();
            Assert.AreEqual(0, PlayerPrefs.GetInt(GainsKey, -1), "gains key after the second click");
            Assert.AreEqual(0, PlayerPrefs.GetInt(ChampionKey, -1), "champion key after the second click");
            Assert.IsFalse(UiDisplayPreferences.GainsFoldedStored, "gains stored again");
            Assert.IsFalse(UiDisplayPreferences.ChampionFoldedStored, "champion stored again");
            UiDisplayPreferences.StoreGainsFolded(true);
            Assert.IsTrue(UiDisplayPreferences.GainsFoldedStored, "StoreGainsFolded alone");
            Assert.IsFalse(UiDisplayPreferences.ChampionFoldedStored, "the other key is untouched");
            UiDisplayPreferences.StoreChampionFolded(true);
            Assert.IsTrue(UiDisplayPreferences.ChampionFoldedStored, "StoreChampionFolded");
        }

        [Test]
        public void TheStoredValuesFoldAFreshHudThroughApplyFoldsAndNeverWrite()
        {
            UiDisplayPreferences.StoreGainsFolded(true);
            UiDisplayPreferences.StoreChampionFolded(true);
            DestroyRig();
            BuildRig();
            Invoke(_hud, "ApplyFolds", UiDisplayPreferences.GainsFoldedStored, UiDisplayPreferences.ChampionFoldedStored);
            Assert.IsTrue(_hud.GainsFolded, "gains flag");
            Assert.IsTrue(_hud.ChampionFolded, "champion flag");
            Assert.AreEqual(63f, PlateHeight(), .01f, "plate folded with no summary");
            Assert.IsFalse(_hud.GainedStripVisible, "no summary: the strip is not shown");
            Assert.AreEqual(28f, ChampionHeight(), .01f, "champion folded");
            Invoke(_hud, "ApplyFolds", false, false);
            Assert.AreEqual(148f, PlateHeight(), .01f, "plate open");
            Assert.AreEqual(126f, ChampionHeight(), .01f, "champion open");
            Assert.AreEqual(1, PlayerPrefs.GetInt(GainsKey, -1), "ApplyFolds never wrote the gains key");
            Assert.AreEqual(1, PlayerPrefs.GetInt(ChampionKey, -1), "ApplyFolds never wrote the champion key");
        }

        [Test]
        public void TheHeadersAreRealClickTargetsAndTheGainsOneIsOnlyAsWideAsItsText()
        {
            DeliverSummary();
            AssertTarget(Find(Gains + "/HitBox"), () => _hud.GainsFolded, "HitBox");
            AssertTarget(Find("ChampionCombatHud/HeadToggle"), () => _hud.ChampionFolded, "HeadToggle");
            var hit = (RectTransform)Find(Gains + "/HitBox");
            var band = (RectTransform)Find(Gains);
            float rightPart = ScreenBounds(band).xMax - 2f;
            Assert.Greater(rightPart, ScreenBounds(hit).xMax, "the probe lies right of the hit box");
            // Image.Raycast does no rectangle test (the GraphicRaycaster filters by rect first), so the hit box rect is asked.
            Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(hit, ScreenBounds(hit).center, null), "the hit box rect holds its own centre");
            Assert.IsFalse(RectTransformUtility.RectangleContainsScreenPoint(hit, new Vector2(rightPart, ScreenBounds(band).center.y), null), "the rest of the band is outside the hit box, so click-through");
        }

        /// <summary>
        /// Standing guard (P-UI-GUARDS-1), green on the base. The rest of the GAINED band takes no pointer: of every graphic in the band, the
        /// only one that takes the pointer is the hit box's Image, and no graphic of the band or its ancestors up to the vitals panel does
        /// (a raycast-target label or plate there would swallow attack clicks over the band).
        /// </summary>
        [Test]
        public void NothingInTheGainedBandTakesThePointerExceptTheHitBox()
        {
            DeliverSummary();
            var band = Find(Gains);
            var hit = Find(Gains + "/HitBox").GetComponent<Image>();
            Assert.IsNotNull(hit, "set-up: the hit box is an Image");
            Assert.IsTrue(hit.raycastTarget, "set-up: the hit box takes the pointer");
            Assert.IsTrue(band.gameObject.activeInHierarchy, "set-up: the band is shown with a summary");
            var inside = band.GetComponentsInChildren<Graphic>(true);
            Assert.GreaterOrEqual(inside.Length, 2, "set-up: the band has the hit box and at least its label");
            var takers = new List<string>();
            foreach (var graphic in inside)
                if (graphic.gameObject.activeInHierarchy && graphic.raycastTarget && graphic != hit) takers.Add(graphic.transform.name);
            Assert.IsEmpty(takers, "no other graphic in the band takes the pointer: " + string.Join(", ", takers));

            var vitals = Find("VitalsPanel");
            var above = new List<string>();
            for (Transform t = band; t != null; t = t.parent)
            {
                foreach (var graphic in t.GetComponents<Graphic>())
                    if (graphic.raycastTarget) above.Add(t.name);
                if (t == vitals) break;
            }
            Assert.IsEmpty(above, "no graphic of the band or its ancestors up to the vitals panel takes the pointer: " + string.Join(", ", above));
        }

        void AssertTarget(Transform header, Func<bool> flag, string name)
        {
            var image = header.GetComponent<Image>(); var button = header.GetComponent<Button>();
            Assert.IsNotNull(image, name + " has an Image");
            Assert.IsNotNull(button, name + " has a Button");
            Assert.IsTrue(image.enabled, name + " image enabled");
            Assert.IsTrue(image.raycastTarget, name + " raycast target");
            Assert.IsFalse(image.canvasRenderer.cullTransparentMesh, name + " is not culled when clear");
            Assert.AreSame(image, button.targetGraphic, name + " target graphic");
            Assert.IsTrue(button.interactable, name + " interactable");
            Assert.IsTrue(image.Raycast(ScreenBounds((RectTransform)header).center, null), name + " takes a point at its centre");
            bool before = flag();
            Click(header);
            Assert.AreNotEqual(before, flag(), name + " click flips the flag");
        }

        [Test]
        public void TheHeaderSaysWhatIsHidden()
        {
            DeliverSummary();
            Invoke(_hud, "ApplyFolds", true, false);
            Assert.AreEqual("GAINED · 3 ▼", HeaderText().text, "folded with three");
            var zeros = DeliverSummary(0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual("GAINED · 0 ▼", HeaderText().text, "folded with none");
            var hint = Find(Gains + "/HitBox").GetComponent<HudIconHint>();
            Assert.IsNotNull(hint, "the hit box carries the hint");
            Assert.AreEqual(LeftEdgeText.GainedHint(BuildHudText.Cells(zeros), true), hint.Text, "the folded hint");
            Invoke(_hud, "ApplyFolds", false, false);
            Assert.AreEqual("GAINED ▲", HeaderText().text, "open again");
        }

        [Test]
        public void ASteadyFoldedFrameAllocatesNothingAndWritesNoRect()
        {
            DeliverSummary();
            DeliverQuest(Quest(1, _serverMs + 134000));
            Invoke(_hud, "ApplyFolds", true, true);
            Assert.AreEqual(63f, PlateHeight(), .01f, "set-up: the plate is folded");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickTerrain", flags));
            var floor = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), _hud, typeof(MatchHud).GetMethod("TrackerFloor", flags));
            var host = new GameObject("tracker host", typeof(RectTransform)); _extra.Add(host);
            host.transform.SetParent(Stage(), false);
            var view = new QuestTrackerView(host.transform);
            float dock = (float)Invoke(_hud, "PlateChainBottom") + HudRegions.TrackerGap;
            Action frame = () => { tick(); view.Refresh(_session, true, dock, floor(), .016f); };
            for (int i = 0; i < 6; i++) frame();
            AllocationMeter.AssertNone(frame, 200, "a folded frame");
            var plate = R("_vitalsPanel"); var champion = R("_championHud");
            plate.sizeDelta += new Vector2(0, 7f); champion.sizeDelta += new Vector2(0, 7f);
            Frame(); Frame(); Frame();
            Assert.AreEqual(70f, plate.sizeDelta.y, .01f, "no frame wrote the plate's height");
            Assert.AreEqual(35f, champion.sizeDelta.y, .01f, "no frame wrote the champion's height");
            plate.sizeDelta -= new Vector2(0, 7f); champion.sizeDelta -= new Vector2(0, 7f);
        }

        [Test]
        public void TheChipRowAndTheHuntCardFollowOnTheClick()
        {
            // Never Frame() here: TickTerrain would overwrite the explicit terrain state.
            DeliverQuest(Quest(1, _serverMs + 134000));
            Snapshot(MatchPhase.Active, _serverMs + 50, (byte)FactionId.Sentenced);
            DeliverSummary();
            _hud.ShowTerrain(true, 4, MapBlob.GroundTerrainAll, FactionId.Sentenced);
            Invoke(_hud, "TickPlayerFlows");
            Assert.IsTrue(_hud.HuntPanel.IsVisible, "set-up: the hunt card shows");
            Assert.IsTrue(Tracker().gameObject.activeSelf, "set-up: the tracker shows");
            var row = R("_terrainRow");
            Assert.AreEqual(162f, M(row).Top, .01f, "set-up: the chip row under the open plate");
            ClickGains();
            Assert.AreEqual(77f, M(row).Top, .01f, "chip row follows in the click frame");
            Assert.AreEqual(M(row).Bottom + 4f, M(_hud.HuntPanel.Root).Top, .01f, "hunt card under the row");
            Assert.AreEqual(_hud.HuntPanel.Bottom, (float)Invoke(_hud, "PlateChainBottom"), .01f, "chain bottom is the card's");
            Invoke(_hud, "TickPlayerFlows");
            Assert.AreEqual(_hud.HuntPanel.Bottom + 8f, M(Tracker()).Top, .6f, "tracker under the card");
            ClickGains();
            Assert.AreEqual(162f, M(row).Top, .01f, "chip row back");
        }

        [Test]
        public void ARebuiltHudIgnoresTheStoredValuesInEditMode()
        {
            // A standing guard, green on the stub on purpose: no EditMode rig depends on the PlayerPrefs of the machine it runs on.
            PlayerPrefs.SetInt(GainsKey, 1); PlayerPrefs.SetInt(ChampionKey, 1);
            DestroyRig();
            BuildRig();
            Assert.IsFalse(_hud.GainsFolded, "gains flag");
            Assert.IsFalse(_hud.ChampionFolded, "champion flag");
            Assert.AreEqual(148f, PlateHeight(), .01f, "plate");
            Assert.AreEqual(126f, ChampionHeight(), .01f, "champion");
        }

        [Test]
        public void TheGainsHitBoxIsAsWideAsItsWidestHeaderAtTheCurrentTextSize()
        {
            DeliverSummary();
            var settings = Field(_hud, "_showcaseSettings");
            Assert.IsNotNull(settings, "rig: the HUD has its settings object");
            _priorScale = (float)settings.GetType().GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings);
            SetScale(1f);
            var label = Find(Gains + "/Label").GetComponent<TMP_Text>();
            var hit = (RectTransform)Find(Gains + "/HitBox");
            Assert.IsTrue(label.gameObject.activeInHierarchy, "the label is active, so it measures true");
            float w1 = M(hit).Width;
            Assert.AreEqual(WantedWidth(label), w1, .01f, "width at scale 1");
            Assert.Less(w1, M((RectTransform)Find(Gains)).Width / 2f, "under half the band");
            Invoke(_hud, "ApplyFolds", true, false);
            DeliverSummary(1, 1, 1, 1, 1, 1, 1, 1, 1);
            Assert.AreEqual(w1, M(hit).Width, .01f, "a fold and a new count leave the width");
            Assert.AreEqual(0f, M(hit).Left - M((RectTransform)Find(Gains)).Left, .01f, "at the left of the band");
            Assert.AreEqual(14f, M(hit).Height, .01f, "14 high");
            SetScale(1.4f);
            Assert.IsTrue(label.gameObject.activeInHierarchy, "still active");
            Assert.AreEqual(WantedWidth(label), M(hit).Width, .01f, "width at scale 1.4");
            Assert.Greater(M(hit).Width, w1, "wider at 1.4");
        }

        void SetScale(float scale)
        {
            var settings = Field(_hud, "_showcaseSettings");
            settings.GetType().GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, scale);
            Invoke(_hud, "ApplyTextScale");
        }

        static float WantedWidth(TMP_Text label)
        {
            float widest = 0f;
            foreach (string form in LeftEdgeText.GainedHeaderForms()) widest = Math.Max(widest, label.GetPreferredValues(form).x);
            return Mathf.Ceil(widest) + label.margin.x + label.margin.z;
        }

        static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        static object Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
    }
}
