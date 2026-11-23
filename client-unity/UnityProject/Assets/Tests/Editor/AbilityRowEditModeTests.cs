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
    /// P-UI-ABILITY-ROW-1: the in-match action row. Weapon 1, swap chip, weapon 2, R, Shift, Q on one row built from HudRegions; the quest
    /// spells are ability tiles inside it; nothing floats above it and the pickup pill and quest launcher share one prompt lane; the weapon
    /// slots carry no digit key cap; a swap pulses the two slots; the row shrinks on a narrow stage. Two rigs: the layout rig builds the HUD
    /// from pieces as HudLegacyLayoutEditModeTests does, the full rig is a whole MatchHud on a real MatchClient over a fake transport as
    /// PlayerFlowsEditModeTests does. Nothing here reads a clock: EditMode time does not advance.
    /// </summary>
    public sealed class AbilityRowEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float Eps = .01f;
        static readonly Vector2[] Windows = { new Vector2(1280, 800), new Vector2(1920, 1080), new Vector2(2560, 1440) };
        static readonly Vector2[] TwoWindows = { new Vector2(1280, 800), new Vector2(1920, 1080) };

        // ------------------------------------------------------------------ the layout rig

        GameObject _canvas, _hudOwner;
        MatchHud _layoutHud;
        RectTransform _layoutStage;
        TacticalMapController _map;
        UiTheme _savedTheme;
        float _savedTextScale = float.NaN;

        void BuildLayoutRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _canvas = new GameObject("ability row canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _hudOwner = new GameObject("ability row hud owner");
            _layoutHud = _hudOwner.AddComponent<MatchHud>();
            SetF(_layoutHud, "_root", _canvas.GetComponent<RectTransform>());
            Call(_layoutHud, "EnsureStage");
            Call(_layoutHud, "CreateTopAndToastPills");
            Call(_layoutHud, "CreateLegacyChrome");
            Call(_layoutHud, "CreateCombatHud");
            _layoutStage = (RectTransform)GetF(_layoutHud, "_stage");
            var prefs = new UiDisplayPreferences();
            SetF(_layoutHud, "_uiPrefs", prefs);
            _map = _hudOwner.AddComponent<TacticalMapController>();
            _map.Initialize(null, _canvas.transform, prefs, _layoutStage);
            SetF(_layoutHud, "_map", _map);
        }

        void LayoutAt(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)GetF(_layoutHud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)GetF(_layoutHud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        // ------------------------------------------------------------------ the full rig

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

        void BuildFullRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Ability row events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Ability row fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://ability/ws", Profile = "ability", Now = () => 1000 });
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
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
            if (_hudOwner != null) UnityEngine.Object.DestroyImmediate(_hudOwner);
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            if (!float.IsNaN(_savedTextScale) && ShowcaseSettings.Current != null)
                typeof(ShowcaseSettings).GetField("_textScale", All).SetValue(ShowcaseSettings.Current, _savedTextScale);
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

        /// <summary>Element i of the ability row as a rect on the stage (HudRegions.AbilityElement placed inside HudRegions.AbilityRow).</summary>
        static HudStageRect RowElement(int i, float stageWidth, float stageHeight)
        {
            var row = HudRegions.AbilityRow().On(stageWidth, stageHeight);
            var e = HudRegions.AbilityElement(i).On(row.Width, row.Height);
            return new HudStageRect(row.Left + e.Left, row.Top + e.Top, e.Width, e.Height);
        }

        RectTransform FullStage => (RectTransform)GetF(_hud, "_stage");
        RectTransform FullBar => Child(FullStage, "PlayerHUD/CombatActions");
        RectTransform Tile(string name) => Child(FullBar, name);

        static string Text(Transform tile, string box) => Child(tile, box + "/Label").GetComponent<TMP_Text>().text;
        static bool NameShown(Transform tile) => Child(tile, "Name").gameObject.activeSelf && Text(tile, "Name").Length != 0;
        static bool WedgeOn(Transform tile) { var s = Child(tile, "CooldownSweep").GetComponent<Image>(); return s.enabled && s.gameObject.activeSelf; }
        static float WedgeFill(Transform tile) => Child(tile, "CooldownSweep").GetComponent<Image>().fillAmount;
        static Color FrameColour(Transform tile) => Child(tile, "Frame").GetComponent<Image>().color;
        static Image Surface(Transform tile) => tile.GetComponent<Image>();

        static object GetF(object o, string name)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            return f.GetValue(o);
        }
        static void SetF(object o, string name, object value)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            f.SetValue(o, value);
        }
        static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, All);
            Assert.That(m, Is.Not.Null, "missing method " + name);
            return m.Invoke(o, args.Length == 0 ? null : args);
        }

        // ------------------------------------------------------------------ 1. the row

        [TestCaseSource(nameof(Windows))]
        public void TheRowHoldsSixElementsLeftToRight(Vector2 window)
        {
            BuildLayoutRig(); LayoutAt(window);
            var bar = Child(_layoutStage, "PlayerHUD/CombatActions");
            float w = _layoutStage.rect.width, h = _layoutStage.rect.height;
            var mBar = Measure(_layoutStage, bar);
            Assert.That(mBar.Width, Is.EqualTo(458f).Within(Eps), "the row is 458 wide");
            Assert.That(mBar.Left, Is.EqualTo(mBar.Right).Within(Eps), "centred");
            Assert.That(mBar.Bottom, Is.EqualTo(14f).Within(Eps), "14 above the stage bottom");
            string[] names = { "Slot1", "QSwap", "Slot2", "WishTile", "QuestSpell1", "QuestSpell2" };
            for (int i = 0; i < names.Length; i++)
                AssertRect(Measure(_layoutStage, Child(bar, names[i])), RowElement(i, w, h), names[i]);
        }

        // ------------------------------------------------------------------ 2. nothing floats above the row

        [TestCaseSource(nameof(TwoWindows))]
        public void NothingFloatsAboveTheRow(Vector2 window)
        {
            BuildFullRig(); LayoutFull(window);
            var stage = FullStage;
            float w = stage.rect.width, h = stage.rect.height;
            var box = Child(stage, "PlayerHUD/QuestRewards");
            AssertRect(Measure(stage, box), HudRegions.Prompt().On(w, h), "QuestRewards");
            Assert.That(box.childCount, Is.EqualTo(1), "the box holds only the launcher");
            Assert.That(box.GetChild(0).name, Is.EqualTo("QuestActionLauncher"));
            AssertRect(Measure(stage, Child(FullBar, "Pickup")), HudRegions.Prompt().On(w, h), "Pickup alone");
            Assert.That(box.Find("QuestSpell1"), Is.Null, "the spell tiles left the floating box");
            Assert.That(box.Find("QuestSpell2"), Is.Null);
            Assert.That(FullBar.Find("QuestSpell1"), Is.Not.Null, "they are in the row");
        }

        // ------------------------------------------------------------------ 3. one prompt lane

        [Test]
        public void APickupAndAQuestActionShareThePromptLane()
        {
            BuildFullRig(); var window = new Vector2(1280, 800); LayoutFull(window);
            var stage = FullStage; float w = stage.rect.width, h = stage.rect.height;
            var box = Child(stage, "PlayerHUD/QuestRewards"); var launcher = Child(box, "QuestActionLauncher"); var pill = Child(FullBar, "Pickup");

            Snap(10, 100); Items(10, NearbyAction()); Frame();
            Assert.That(launcher.gameObject.activeInHierarchy, Is.True, "set-up: the nearby quest action shows the launcher");
            var alone = Measure(stage, launcher);
            Assert.That(alone.Left, Is.EqualTo(alone.Right).Within(Eps), "the launcher is centred");
            AssertRect(Measure(stage, box), HudRegions.Prompt().On(w, h), "box alone");

            Snap(11, 150); Items(11, PickupInReach(true), NearbyAction());   // state items go in ascending key order: Props (2) before Quests (10) Frame();
            Assert.That(pill.gameObject.activeInHierarchy, Is.True, "set-up: a pickup in reach shows the pill");
            Assert.That(launcher.gameObject.activeInHierarchy, Is.True, "the pickup does not hide the launcher");
            var left = HudRegions.PromptPair(0).On(w, h); var right = HudRegions.PromptPair(1).On(w, h);
            AssertRect(Measure(stage, pill), left, "pill beside the launcher");
            AssertRect(Measure(stage, box), right, "box beside the pill");
            Assert.That(left.Overlaps(right), Is.False);
            var pillRect = Measure(stage, pill); var boxRect = Measure(stage, box);
            Assert.That(pillRect.Left + pillRect.Width, Is.LessThanOrEqualTo(boxRect.Left + Eps), "they do not overlap");

            Snap(12, 200); Items(12, PickupInReach(false), NearbyAction()); Frame();
            Assert.That(pill.gameObject.activeInHierarchy, Is.False, "set-up: the pickup is gone");
            Assert.That(launcher.gameObject.activeInHierarchy, Is.True);
            AssertRect(Measure(stage, box), HudRegions.Prompt().On(w, h), "box centred again");
            var again = Measure(stage, launcher);
            Assert.That(again.Left, Is.EqualTo(again.Right).Within(Eps), "the launcher is centred again");
        }

        // ------------------------------------------------------------------ 4. an empty tile

        [Test]
        public void AnEmptySpellTileIsASolidPlateWithADimKeyCap()
        {
            BuildLayoutRig(); LayoutAt(Windows[0]);
            foreach (string name in new[] { "QuestSpell1", "QuestSpell2" })
            {
                var tile = Child(_layoutStage, "PlayerHUD/CombatActions/" + name);
                Assert.That(tile.gameObject.activeInHierarchy, Is.True, name + " is visible with the bar");
                Assert.That(Text(tile, "Name"), Is.Empty, name + " name");
                Assert.That(Text(tile, "Seconds"), Is.Empty, name + " seconds");
                Assert.That(Text(tile, "Charges"), Is.Empty, name + " badge");
                Assert.That(tile.GetComponent<Button>(), Is.Not.Null, name + " carries the hint button");
                Assert.That(tile.GetComponent<Button>().interactable, Is.False, name + " is not interactable while empty");
                Assert.That(Surface(tile).color, Is.EqualTo((Color)UiTheme.Current.SurfaceRaised), name + " surface is the solid plate");
                Assert.That(WedgeOn(tile), Is.False, name + " wedge");
            }
            Assert.That(Text(Child(_layoutStage, "PlayerHUD/CombatActions/QuestSpell1"), "Key"), Is.EqualTo("Shift"));
            Assert.That(Text(Child(_layoutStage, "PlayerHUD/CombatActions/QuestSpell2"), "Key"), Is.EqualTo("Q"));
        }

        // ------------------------------------------------------------------ 5. a delivered spell

        [Test]
        public void ADeliveredQuestSpellShowsNameChargesAndCountdown()
        {
            BuildFullRig(); LayoutFull(Windows[0]);
            var theme = UiTheme.Current;
            Snap(10, 1000);
            Items(10, Quests(new[] { new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 6000 } }));
            Frame();
            Assert.That(_session.ServerTimeMs, Is.EqualTo(1000d), "set-up: the server time is the delivered one");
            Assert.That(_session.Collections.Quests.Spells.Count, Is.EqualTo(1), "set-up: the spell was delivered");
            var one = Tile("QuestSpell1"); var two = Tile("QuestSpell2");
            Assert.That(Text(one, "Charges"), Is.EqualTo("2"), "badge");
            Assert.That(Text(one, "Seconds"), Is.EqualTo("5"), "5 s left");
            Assert.That(NameShown(one), Is.False, "the name is hidden while cooling");
            Assert.That(WedgeOn(one), Is.True, "wedge on");
            Assert.That(WedgeFill(one), Is.EqualTo(1f).Within(.02f), "full wedge at first sight");
            Assert.That(FrameColour(one), Is.EqualTo((Color)theme.Border), "cooling frame");

            Snap(11, 3500); Frame();
            Assert.That(Text(one, "Seconds"), Is.EqualTo("3"), "2.5 s left rounds up to 3");
            Assert.That(WedgeFill(one), Is.EqualTo(.5f).Within(.02f), "half the span is left");

            Snap(12, 7000); Frame();
            Assert.That(NameShown(one), Is.True, "ready: the name shows");
            Assert.That(Text(one, "Name"), Is.EqualTo("Shoulder Charge"));
            Assert.That(Text(one, "Seconds"), Is.Empty);
            Assert.That(WedgeOn(one), Is.False, "wedge off");
            Assert.That(FrameColour(one), Is.EqualTo((Color)theme.BorderStrong), "ready frame");
            Assert.That(Surface(one).color, Is.EqualTo((Color)theme.SurfaceRaised), "the surface is exactly the table's colour");
            Assert.That(Text(one, "Charges"), Is.EqualTo("2"));

            Assert.That(NameShown(two), Is.False, "Q stays empty");
            Assert.That(Text(two, "Charges"), Is.Empty);
            Assert.That(Surface(two).color, Is.EqualTo((Color)theme.SurfaceRaised));
            Assert.That(two.GetComponent<Button>().interactable, Is.False);
        }

        // ------------------------------------------------------------------ 6. the equipped line

        [Test]
        public void TheEquippedLineStillListsBothSpells()
        {
            BuildFullRig(); LayoutFull(Windows[0]);
            var equipped = (TMP_Text)GetF(_hud, "_questChoiceSpells");
            Snap(9, 500); Items(9, Quests()); Frame();
            Assert.That(equipped.text, Is.EqualTo("SHIFT · Empty\nQ · Empty"), "with a quest view and no spell both slots read Empty");
            Snap(10, 7000);
            Items(10, Quests(new[] { new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 6000 } }));
            Frame();
            Assert.That(_session.ServerTimeMs, Is.EqualTo(7000d), "set-up: the server time is the delivered one");
            Assert.That(equipped.text, Is.EqualTo("SHIFT · Shoulder Charge 2 charges · Ready\nQ · Empty"));
        }

        // ------------------------------------------------------------------ 7. no digit caps on the weapon slots

        /// <summary>
        /// P-QUEST25-CONTROLS (owner direction 2026-11-18): Space swaps weapons and the digit keys do not, so the slots carry no key cap. The base
        /// builds the cap in CreateWeaponBar as the box "Key" under each slot, with a child "Label" showing "1" or "2". The swap chip keeps its
        /// Space cap.
        /// </summary>
        [Test]
        public void TheWeaponSlotsCarryNoDigitCap()
        {
            BuildLayoutRig(); LayoutAt(Windows[0]);
            var bar = Child(_layoutStage, "PlayerHUD/CombatActions");
            for (int i = 0; i < 2; i++)
            {
                var slot = Child(bar, "Slot" + (i + 1));
                var cap = slot.Find("Key/Label");
                Assert.That(cap == null || cap.GetComponent<TMP_Text>().text.Length == 0, Is.True,
                    "Slot" + (i + 1) + "/Key/Label is absent or empty (it shows '" + (cap == null ? "" : cap.GetComponent<TMP_Text>().text) + "')");
                foreach (var label in slot.GetComponentsInChildren<TMP_Text>(true))
                {
                    string shown = label.text == null ? "" : label.text.Trim();
                    Assert.That(shown, Is.Not.EqualTo("1").And.Not.EqualTo("2"),
                        "no label under Slot" + (i + 1) + " shows a digit cap, but '" + label.transform.parent.name + "/" + label.name + "' reads '" + shown + "'");
                }
            }
            Assert.That(Text(Child(bar, "QSwap"), "Key"), Is.EqualTo("Space"), "the swap chip keeps its Space cap");
        }

        // ------------------------------------------------------------------ 8. the swap pulse

        [Test]
        public void ASwapWithoutASlotPulsesBothSlotsAndNeverTheBar()
        {
            BuildLayoutRig(); LayoutAt(Windows[0]);
            var hud = _layoutHud;
            double local = 5000; hud.HudLocalMs = () => local;
            var bar = Child(_layoutStage, "PlayerHUD/CombatActions");
            var slots = (Button[])GetF(hud, "_slots");
            var pulses = GetF(hud, "_pulses") as HudPulses;
            Assert.That(pulses, Is.Not.Null, "MatchHud._pulses is the one HudPulses");
            var barScale = bar.localScale;

            Call(hud, "InputCue", WireCommand.Swap(1));
            Assert.That(pulses.Count, Is.EqualTo(1), "a swap with a slot pulses that slot (as before)");
            local = 5075; Call(hud, "TickActionRow");
            Assert.That(slots[1].transform.localScale.x, Is.GreaterThan(1f), "the slot named pops");
            Assert.That(slots[0].transform.localScale.x, Is.EqualTo(1f), "the other slot does not");
            Call(hud, "ClearPulses");
            Assert.That(pulses.Count, Is.EqualTo(0), "ClearPulses ends them all");

            local = 6000;
            Call(hud, "InputCue", WireCommand.Swap());
            Assert.That(pulses.Count, Is.EqualTo(2), "a swap without a slot pulses the two slots, and no third element (never the bar)");
            local = 6075; Call(hud, "TickActionRow");
            foreach (var slot in slots) Assert.That(slot.transform.localScale.x, Is.GreaterThan(1f), "both slots pop");
            Assert.That(bar.localScale, Is.EqualTo(barScale), "the bar keeps the scale the layout gave it");
        }

        // ------------------------------------------------------------------ 9. a narrow stage

        [Test]
        public void TheRowShrinksOnANarrowStage()
        {
            BuildLayoutRig();
            LayoutAt(new Vector2(1280, 800));
            var bar = Child(_layoutStage, "PlayerHUD/CombatActions");
            Assert.That(bar.localScale.x, Is.EqualTo(1f), "a wide stage keeps the row at scale 1");
            var wide = HudLayout.Viewport(1280, 800);
            Assert.That(Measure(_layoutStage, _map.MinimapRoot).Width, Is.EqualTo(HudLayout.MinimapSize(wide.UiWidth, wide.UiHeight, 1f)).Within(Eps), "minimap at 1280x800");

            LayoutAt(new Vector2(700, 600));
            var v = HudLayout.Viewport(700, 600);
            float w = _layoutStage.rect.width;
            Assert.That(w, Is.EqualTo(v.UiWidth).Within(Eps), "set-up: the stage is the narrow one");
            Assert.That(bar.localScale.x, Is.LessThan(1f), "the row shrinks");
            Assert.That(bar.localScale.x, Is.GreaterThanOrEqualTo(.6f - .001f));
            var champion = Measure(_layoutStage, Child(_layoutStage, "PlayerHUD/ChampionCombatHud"));
            var minimap = Measure(_layoutStage, _map.MinimapRoot);
            var barRect = Measure(_layoutStage, bar);
            Assert.That(barRect.Left, Is.GreaterThanOrEqualTo(champion.Left + champion.Width + 8f - .5f), "8 clear of the champion panel");
            Assert.That(w - barRect.Right, Is.LessThanOrEqualTo(minimap.Left - 8f + .5f), "8 clear of the minimap");
            Assert.That(barRect.Left, Is.EqualTo(barRect.Right).Within(Eps), "still centred");
            Assert.That(minimap.Width, Is.EqualTo(HudLayout.MinimapSize(v.UiWidth, v.UiHeight, 1f)).Within(Eps), "the minimap keeps its size");
        }

        // ------------------------------------------------------------------ 10. no garbage

        [Test]
        public void AnUnchangedTileAllocatesNothing()
        {
            BuildLayoutRig();
            var tile = new AbilityTile(_layoutStage, "GarbageTile", HudRegions.AbilityElement(4), 34f, "Shift", _canvas.transform);
            Assert.That(tile.Root, Is.Not.Null, "the tile builds its root");
            Assert.That(tile.SecondsLabel, Is.Not.Null, "and its seconds label");
            var face = new AbilityFace(AbilityPhase.Cooling, .42f, 5, 2, -1f);
            tile.Show(face, "Hook", "Shift · Hook\n2 charges · 5s");
            Assert.That(tile.SecondsLabel.text, Is.EqualTo("5"));
            Assert.That(tile.BadgeLabel.text, Is.EqualTo("2"));
            Assert.That(tile.Sweep.fillAmount, Is.EqualTo(.42f).Within(.001f));
            Action show = () => tile.Show(face, "Hook", "Shift · Hook\n2 charges · 5s");
            for (int i = 0; i < 5; i++) show();   // warm up
            AllocationMeter.AssertNone(show, 200, "200 unchanged Show calls");
            tile.Show(new AbilityFace(AbilityPhase.Ready, 0f, 0, 2, -1f), "Hook", "Shift · Hook");
            Assert.That(tile.SecondsLabel.text, Is.Empty, "a changed face still redraws");
        }

        [Test]
        public void ASteadyQuestRefreshFrameAllocatesNothing()
        {
            BuildFullRig(); LayoutFull(Windows[0]);
            Snap(10, 1000);
            Items(10, Quests(new[] { new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 6000 } }));
            Frame();
            Assert.That(Text(Tile("QuestSpell1"), "Seconds"), Is.EqualTo("5"), "set-up: the spell is drawn");
            var refresh = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));
            for (int i = 0; i < 5; i++) refresh();   // warm up
            AllocationMeter.AssertNone(refresh, 200, "200 steady refresh frames");
        }

        // ------------------------------------------------------------------ 11. the labels fit

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
        public void TheLongestSpellNamesFitTheTile(float textScale)
        {
            BuildLayoutRig(); LayoutAt(Windows[0]);
            MythicUi.ApplyTextScale(_canvas.transform, textScale);
            var problems = new List<string>();
            foreach (string tileName in new[] { "QuestSpell1", "QuestSpell2" })
            {
                var tile = Child(_layoutStage, "PlayerHUD/CombatActions/" + tileName);
                void Check(string box, params string[] texts)
                {
                    var label = Child(tile, box + "/Label").GetComponent<TMP_Text>();
                    var follow = label.GetComponent<MythicTextScale>();
                    if (follow == null) { problems.Add(tileName + "/" + box + " has no MythicTextScale"); return; }
                    foreach (string text in texts)
                    {
                        label.text = text; follow.Refit();
                        if (label.overflowMode == TextOverflowModes.Ellipsis || label.overflowMode == TextOverflowModes.Truncate)
                            problems.Add(tileName + "/" + box + " '" + text + "' would be cut (overflow " + label.overflowMode + ")");
                        float size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
                        var rect = label.rectTransform.rect; var need = Needs(label, text, size);
                        if (need.x > rect.width * MythicTextScale.WidthShare + .001f || need.y > rect.height + .01f)
                            problems.Add(tileName + "/" + box + " '" + text + "' at " + size + " needs " + need.x.ToString("0.#") + "x" + need.y.ToString("0.#")
                                + " of " + rect.width.ToString("0.#") + "x" + rect.height.ToString("0.#") + " at text scale " + textScale);
                    }
                }
                Check("Name", "Forget-Me-Not Seed", "Shoulder Charge");
                Check("Seconds", "125");
                Check("Key", "Shift", "Q");
                Check("Charges", "3");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ 12. theme colours

        [Test]
        public void EveryTileGraphicUsesAThemeColour()
        {
            BuildLayoutRig(); LayoutAt(Windows[0]);
            var field = typeof(MatchHud).GetField("_questTiles", All);
            Assert.That(field, Is.Not.Null, "MatchHud keeps its two ability tiles in _questTiles");
            var tiles = (AbilityTile[])field.GetValue(_layoutHud);
            Assert.That(tiles, Is.Not.Null.And.Length.EqualTo(2));
            var theme = UiTheme.Current;
            var faces = new[]
            {
                (AbilityFace.Empty, "", "Shift · Quest spell"),
                (new AbilityFace(AbilityPhase.Ready, 0f, 0, 2, -1f), "Shoulder Charge", "Shift · Shoulder Charge\n2 charges · Ready"),
                (new AbilityFace(AbilityPhase.Cooling, .5f, 3, 2, -1f), "Shoulder Charge", "Shift · Shoulder Charge\n2 charges · 3s"),
            };
            var bad = new List<string>();
            foreach (var (face, name, hint) in faces)
                foreach (var tile in tiles)
                {
                    tile.Show(face, name, hint);
                    foreach (var graphic in tile.Root.GetComponentsInChildren<Graphic>(true))
                    {
                        if (graphic is TMP_SubMeshUI) continue;
                        bool tintOnly = graphic is RawImage || graphic is Image && ((Image)graphic).sprite != null || !(graphic is Image) && !(graphic is TMP_Text);
                        bool whiteWash = graphic.color.r >= .999f && graphic.color.g >= .999f && graphic.color.b >= .999f;
                        if (tintOnly && whiteWash) continue;
                        if (!theme.Owns(graphic.color)) bad.Add(tile.Root.name + "/" + graphic.name + " " + face.Phase + " " + (Color32)graphic.color);
                    }
                }
            Assert.That(bad, Is.Empty, "tile graphics outside the theme:\n" + string.Join("\n", bad));
        }
    }
}
