using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-ABILITY-MOTION-1: the action row moves on every frame. The cooldown wedges of R, Shift and Q and the Wish cast bar are drawn from a
    /// HUD clock that runs ahead of the newest snapshot (at most 150 ms), the attack timer is a bar on the weapon in hand, a press pops its element
    /// about its centre through the one HudPulses, and the Wish tile has one scale writer. The rig is the full one of AbilityRowEditModeTests (a
    /// whole MatchHud on a real MatchClient over a fake transport). EditMode time does not advance, so every time the row reads goes through
    /// MatchHud.HudLocalMs, which these tests drive: a time-stepping test calls Frame() once at the snapshot's local time before it moves _local.
    /// </summary>
    public sealed class AbilityMotionEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float Eps = .01f;
        static readonly Vector2 Window = new Vector2(1280, 800);

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
        double _local;

        [SetUp]
        public void SetUp() { UiMotion.ReducedOverride = false; }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = null;
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------ the rig

        static void Hello(Transport transport) =>
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });

        /// <summary>The whole HUD on a fake transport, laid out at 1280x800, with the HUD's local clock at 5000 ms and under the test's control.</summary>
        void BuildRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Ability motion events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Ability motion fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://motion/ws", Profile = "motion", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            Hello(_transport);
            Snap(1, 50);
            float factor = HudLayout.CanvasScalerFactor(Window.x, Window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(Window.x, Window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(Window.x, Window.y, factor);
            _local = 5000;
            _hud.HudLocalMs = () => _local;
        }

        static WireSelf Self(WireWishOwner wish, ushort readyInMs) =>
            new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0, ReadyInMs = readyInMs, Wish = wish };

        void Snap(uint tick, double serverMs) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = (byte)MatchPhase.Active, Me = Self(null, 0) });

        /// <summary>A snapshot that carries the owner's Wish (null: none) and the attack timer left (ReadyInMs).</summary>
        void SnapWish(uint tick, double serverMs, WireWishOwner wish, ushort readyInMs = 0) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = (byte)MatchPhase.Active, Me = Self(wish, readyInMs) });

        /// <summary>A valid owner Wish (Blaze, one charge slot); a cast when castStartedAt is not negative.</summary>
        static WireWishOwner Wish(byte charges, double readyAt, double rechargeMs, double castStartedAt = -1, double castLandsAt = 0) =>
            new WireWishOwner
            {
                Ability = 3, MaxCharges = 1, Charges = charges, ReadyAt = readyAt, RechargeMs = rechargeMs,
                Cast = castStartedAt < 0 ? null : new WireWishCast { Ability = 3, StartedAt = castStartedAt, LandsAt = castLandsAt },
            };

        void Items(uint tick, params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = tick, Items = items });

        static InventoryState Inventory(byte activeSlot) => new InventoryState { Value = new WireInventory { ActiveSlot = activeSlot,
            Slot0 = (byte)WeaponId.Sword, Slot1 = (byte)WeaponId.Bow, PotionSmall = 1, PotionLarge = 1 } };

        static QuestsState Spells(params WireQuestSpell[] spells) => new QuestsState { Available = true, View = new WireQuestView { Spells = spells } };

        /// <summary>One frame of the HUD's late update (the quest refresh runs in it, and once more through the player-flow tick).</summary>
        void Frame() { Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        /// <summary>The Wish ready at 50000, then accepted: a snapshot 50 ms later with no charge and the next ready time ahead. Two frames, local +50 ms.</summary>
        void AcceptedCast(double readyAheadMs)
        {
            SnapWish(10, 50000, Wish(1, 40000, 10000)); Frame();
            SnapWish(11, 50050, Wish(0, 50050 + readyAheadMs, 10000)); _local += 50; Frame();
        }

        // ------------------------------------------------------------------ reading the HUD

        RectTransform Stage => (RectTransform)GetF(_hud, "_stage");
        RectTransform Bar => Child(Stage, "PlayerHUD/CombatActions");
        RectTransform Tile(string name) => Child(Bar, name);
        Image Glow => (Image)GetF(_hud, "_wishGlow");

        HudPulses Pulses()
        {
            var pulses = GetF(_hud, "_pulses") as HudPulses;
            Assert.That(pulses, Is.Not.Null, "MatchHud._pulses is a HudPulses");
            return pulses;
        }

        static bool WedgeOn(Transform tile) { var s = Child(tile, "CooldownSweep").GetComponent<Image>(); return s.enabled && s.gameObject.activeSelf; }
        static float WedgeFill(Transform tile) => Child(tile, "CooldownSweep").GetComponent<Image>().fillAmount;
        static bool CastOn(Transform tile) { var s = Child(tile, "Cast").GetComponent<Image>(); return s.enabled && s.gameObject.activeSelf; }
        static float CastFill(Transform tile) => Child(tile, "Cast").GetComponent<Image>().fillAmount;
        static Image AttackBar(Transform slot) => Child(slot, "AttackBar").GetComponent<Image>();
        static bool AttackOn(Transform slot) { var b = AttackBar(slot); return b.enabled && b.gameObject.activeSelf; }

        struct Rect4 { public float Left, Top, Right, Bottom, Width, Height; }

        /// <summary>Insets (left, top, right, bottom) and size of a rect inside the stage, in HUD units.</summary>
        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Right = w / 2f - max.x, Top = h / 2f - max.y, Bottom = min.y + h / 2f, Width = max.x - min.x, Height = max.y - min.y };
        }

        /// <summary>The centre of a rect in the stage's own units.</summary>
        Vector2 Centre(RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 c = Stage.InverseTransformPoint((corners[0] + corners[2]) / 2f);
            return new Vector2(c.x, c.y);
        }

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

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

        // ------------------------------------------------------------------ 1. the Wish wedge

        [Test]
        public void TheWishWedgeMovesBetweenSnapshots()
        {
            BuildRig(); var tile = Tile("WishTile");
            SnapWish(10, 50000, Wish(0, 55000, 10000)); Frame();
            Assert.That(WedgeOn(tile), Is.True, "set-up: the Wish wedge is on while it recharges");
            Assert.That(WedgeFill(tile), Is.EqualTo(.5f).Within(.01f), "half the recharge is left at the snapshot");
            _local += 100; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.49f).Within(.004f), "100 ms later, with no new snapshot, the wedge has moved");
            float last = WedgeFill(tile);
            for (int ms = 200; ms <= 400; ms += 100)
            {
                _local = 5000 + ms; Frame();
                float fill = WedgeFill(tile);
                Assert.That(fill, Is.LessThanOrEqualTo(last + .0001f), "the wedge never rises without a new snapshot (+" + ms + " ms)");
                last = fill;
            }
            Assert.That(WedgeFill(tile), Is.EqualTo(.485f).Within(.003f), "400 ms later the clock has stopped 150 ms past the snapshot");
            Assert.That(WedgeOn(tile), Is.True, "the wedge stays on");

            SnapWish(11, 60000, Wish(0, 65000, 0)); Frame();
            Assert.That(WedgeOn(tile), Is.True, "an unknown recharge length keeps the wedge on");
            Assert.That(WedgeFill(tile), Is.EqualTo(1f).Within(.001f), "at fill 1");
            _local += 100; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(1f).Within(.001f), "and it stays 1");
        }

        // ------------------------------------------------------------------ 2. the cast bar

        [Test]
        public void TheCastBarMovesBetweenSnapshots()
        {
            BuildRig(); var tile = Tile("WishTile");
            SnapWish(10, 50000, Wish(1, 40000, 10000, 50000, 51000)); Frame();
            var bar = Child(tile, "Cast").GetComponent<Image>();
            Assert.That(CastOn(tile), Is.True, "set-up: the cast bar is on while a cast is in flight");
            Assert.That(bar.type, Is.EqualTo(Image.Type.Filled), "set-up: it is a filled image");
            Assert.That(bar.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal), "set-up: it fills horizontally");
            Assert.That(bar.sprite, Is.Not.Null, "set-up: it has a sprite");
            Assert.That(CastFill(tile), Is.EqualTo(0f).Within(.01f), "a cast started with the snapshot is empty");
            _local += 100; Frame();
            Assert.That(CastFill(tile), Is.EqualTo(.1f).Within(.01f), "100 ms into a 1 s cast, with no new snapshot");
            Assert.That(CastOn(tile), Is.True, "still on");
            SnapWish(11, 50100, Wish(1, 40000, 10000)); Frame();
            Assert.That(CastOn(tile), Is.False, "a snapshot with no cast turns the bar off");
        }

        // ------------------------------------------------------------------ 3. a quest spell wedge

        [Test]
        public void AQuestSpellWedgeMovesBetweenSnapshots()
        {
            BuildRig(); var one = Tile("QuestSpell1");
            Snap(10, 1000);
            Items(10, Spells(new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 6000 }));
            Frame();
            Assert.That(_session.Collections.Quests.Spells.Count, Is.EqualTo(1), "set-up: the spell was delivered");
            Assert.That(WedgeOn(one), Is.True, "set-up: the wedge is on");
            Assert.That(WedgeFill(one), Is.EqualTo(1f).Within(.02f), "set-up: full at first sight (5 s of 5 s)");
            _local += 100; Frame();
            Assert.That(WedgeFill(one), Is.EqualTo(.98f).Within(.004f), "100 ms later the wedge has moved");
            float last = WedgeFill(one);
            foreach (int ms in new[] { 0, 40, 80 })
            {
                _local = 5100 + ms; Frame();
                float fill = WedgeFill(one);
                Assert.That(WedgeOn(one), Is.True, "the wedge is on at +" + ms + " ms");
                Assert.That(fill, Is.LessThanOrEqualTo(last + .0001f), "the wedge never rises (+" + ms + " ms)");
                last = fill;
            }

            Snap(11, 2000);
            Items(11, Spells(new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 2100 }));
            Frame();
            Assert.That(WedgeOn(one), Is.True, "set-up: a spell 100 ms from ready is still cooling");
            _local += 150; Frame();
            Assert.That(WedgeOn(one), Is.True, "past the ready time the wedge stays on until a snapshot says Ready");
            Assert.That(WedgeFill(one), Is.EqualTo(0f).Within(.001f), "at fill 0");
            Snap(12, 2300); Frame();
            Assert.That(WedgeOn(one), Is.False, "a snapshot past ReadyAt switches the wedge off");
        }

        // ------------------------------------------------------------------ 4, 5. the attack bar

        [Test]
        public void TheAttackBarIsOnTheWeaponInHandOnly()
        {
            BuildRig(); var s1 = Tile("Slot1"); var s2 = Tile("Slot2");
            SnapWish(10, 50000, null, 400); Items(10, Inventory(0)); Frame();
            Assert.That(GetF(_hud, "_inventoryShown"), Is.Not.Null, "set-up: the inventory was delivered");
            Assert.That(s1.Find("CooldownSweep"), Is.Null, "Slot1 has no radial sweep any more");
            Assert.That(s2.Find("CooldownSweep"), Is.Null, "Slot2 has no radial sweep any more");
            Assert.That(AttackOn(s1), Is.True, "slot 0 is in hand: its bar is on");
            Assert.That(AttackBar(s1).fillAmount, Is.EqualTo(1f).Within(.01f), "at the start of the timer the bar is full");
            Assert.That(AttackOn(s2), Is.False, "the other slot's bar is off");

            SnapWish(11, 50010, null, 400); Items(11, Inventory(1)); _local += 10; Frame();
            Assert.That(((InventoryView)GetF(_hud, "_inventoryShown")).ActiveSlot, Is.EqualTo(1), "set-up: slot 1 is now in hand");
            Assert.That(AttackOn(s2), Is.True, "slot 1 is in hand: its bar is on");
            Assert.That(AttackOn(s1), Is.False, "and the first slot's is off");
            Assert.That(AttackBar(s2).fillAmount, Is.EqualTo(1f).Within(.01f), "the timer did not restart or shrink");

            var theme = UiTheme.Current;
            foreach (var slot in new[] { s1, s2 })
            {
                var bar = AttackBar(slot);
                Assert.That(bar.sprite, Is.Not.Null, slot.name + " bar has a sprite");
                Assert.That(bar.type, Is.EqualTo(Image.Type.Filled), slot.name + " bar is filled");
                Assert.That(bar.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal), slot.name + " bar fills horizontally");
                Assert.That(bar.fillOrigin, Is.EqualTo(0), slot.name + " bar fills from the left");
                Assert.That(bar.color, Is.EqualTo((Color)theme.Accent), slot.name + " bar is the accent colour");
                Assert.That(bar.raycastTarget, Is.False, slot.name + " bar takes no pointer");
                var m = Measure(Stage, (RectTransform)bar.transform); var s = Measure(Stage, slot);
                Assert.That(m.Height, Is.EqualTo(3f).Within(Eps), slot.name + " bar is 3 high");
                Assert.That(m.Bottom - s.Bottom, Is.EqualTo(4f).Within(Eps), slot.name + " bar is 4 above the slot's bottom");
                Assert.That(m.Left - s.Left, Is.EqualTo(6f).Within(Eps), slot.name + " bar is inset 6 on the left");
                Assert.That(m.Right - s.Right, Is.EqualTo(6f).Within(Eps), slot.name + " bar is inset 6 on the right");
            }
        }

        [Test]
        public void TheAttackBarDrainsBetweenSnapshots()
        {
            BuildRig(); var s1 = Tile("Slot1");
            SnapWish(10, 50000, null, 400); Items(10, Inventory(0)); Frame();
            Assert.That(AttackOn(s1), Is.True, "set-up: the bar is on");
            Assert.That(AttackBar(s1).fillAmount, Is.EqualTo(1f).Within(.01f), "set-up: full at the snapshot");
            _local += 100; Frame();
            Assert.That(AttackBar(s1).fillAmount, Is.EqualTo(.75f).Within(.01f), "100 ms of a 400 ms timer are gone");
            SnapWish(11, 50100, null, 0); Frame();
            Assert.That(AttackOn(s1), Is.False, "a snapshot with the weapon ready turns the bar off");
        }

        // ------------------------------------------------------------------ 6, 7. the pop is about the centre

        [Test]
        public void APressPopsAboutTheCentre()
        {
            BuildRig(); Snap(10, 50000); Frame();
            var cases = new List<(string Name, RectTransform Rect, WireCommand Cue)>
            {
                ("the Wish tile", Tile("WishTile"), WireCommand.Cast(0, 0f, 0f)),
                ("QuestSpell1", Tile("QuestSpell1"), WireCommand.Cast(1, 0f, 0f)),
                ("the potion button", (RectTransform)((Button)GetF(_hud, "_potionButton")).transform, WireCommand.UseConsumable()),
                ("Slot1", Tile("Slot1"), WireCommand.Swap(0)),
            };
            foreach (var c in cases)
            {
                var rect = c.Rect; Vector2 position = rect.anchoredPosition, centre = Centre(rect);
                Assert.That(rect.anchorMin, Is.EqualTo(rect.anchorMax), c.Name + ": set-up: a point-anchored rect, as HudPulses needs");
                double start = _local;
                Call(_hud, "InputCue", c.Cue);
                for (int ms = 10; ms <= 150; ms += 10)
                {
                    _local = start + ms; Call(_hud, "TickActionRow");
                    Assert.That((Centre(rect) - centre).magnitude, Is.LessThan(.01f), c.Name + ": the centre stays put at +" + ms + " ms");
                    if (ms == 80)
                        Assert.That(rect.localScale.x, Is.GreaterThan(1f), c.Name + ": it is popped at half time");
                }
                _local = start + 160; Call(_hud, "TickActionRow");
                Assert.That(rect.localScale.x, Is.EqualTo(1f), c.Name + ": the scale is exactly 1 after the pop");
                Assert.That((rect.anchoredPosition - position).magnitude, Is.LessThan(.001f), c.Name + ": it is back where it was");
                _local = start + 500;
            }
        }

        [Test]
        public void APulseLeavesARePlacedPillWhereItWasPlaced()
        {
            BuildRig(); Snap(10, 50000); Frame();
            var pulses = Pulses();
            var pill = Child(Bar, "Pickup");
            double start = _local;
            Call(_hud, "InputCue", WireCommand.Pickup(new PickupHandle(5)));
            Assert.That(pulses.Count, Is.EqualTo(1), "the pickup cue pulses the pill");
            _local = start + 75; Call(_hud, "TickActionRow");
            Assert.That(pill.localScale.x, Is.GreaterThan(1f), "set-up: the pill is popped at half time");
            Vector2 written = pill.anchoredPosition + new Vector2(112f, 0f);
            pill.anchoredPosition = written;
            _local = start + 160; Call(_hud, "TickActionRow");
            Assert.That((pill.anchoredPosition - written).magnitude, Is.LessThan(.001f), "the pill stays where the other writer put it");
            Assert.That(pill.localScale.x, Is.EqualTo(1f), "scale exactly 1");
            Assert.That(pulses.Count, Is.EqualTo(0), "the pulse is over");
        }

        // ------------------------------------------------------------------ 8, 9, 10, 11. the Wish tile's one scale writer

        [Test]
        public void TheWishTileHasOneScaleWriter()
        {
            BuildRig(); var tile = Tile("WishTile");
            AcceptedCast(10000);
            double start = _local;
            Call(_hud, "InputCue", WireCommand.Cast(0, 0f, 0f));
            for (int ms = 10; ms <= 150; ms += 10)
            {
                _local = start + ms; Frame();
                Assert.That(tile.localScale.x, Is.LessThanOrEqualTo(1f + HudPulses.Pop + .001f), "one pop and no wash scale at +" + ms + " ms");
                Assert.That(Glow.enabled, Is.True, "the wash is on at +" + ms + " ms");
            }
        }

        [Test]
        public void AnAcceptedCastPopsTheWishTileOnce()
        {
            BuildRig(); var pulses = Pulses();
            AcceptedCast(10000); Frame();
            Assert.That(pulses.Count, Is.EqualTo(1), "the frame after the accepting one pops the Wish tile");
            _local += 160; Frame();
            Assert.That(pulses.Count, Is.EqualTo(0), "the pop is over after .16 s");
            Assert.That(Glow.enabled, Is.True, "while the wash is still on");
            foreach (int ms in new[] { 90, 190 })
            {
                _local += ms; Frame();
                Assert.That(pulses.Count, Is.EqualTo(0), "it does not pop again as the wash fades (+" + ms + " ms)");
                Assert.That(Glow.enabled, Is.True, "the wash is still on (+" + ms + " ms)");
            }
        }

        [Test]
        public void TheReadyPulsePopsTheWishTileOnce()
        {
            BuildRig(); var pulses = Pulses();
            SnapWish(10, 50000, Wish(0, 55000, 10000)); Frame();
            SnapWish(11, 55050, Wish(1, 55000, 10000)); _local += 50; Frame();
            _local += 50; Frame();
            Assert.That(pulses.Count, Is.EqualTo(1), "the ready pulse pops the tile once");
            _local += 160; Frame();
            Assert.That(pulses.Count, Is.EqualTo(0), "and the pop is over after .16 s");
        }

        [Test]
        public void ReducedMotionKeepsTheWashAndTheWedgesAndDropsThePop()
        {
            UiMotion.ReducedOverride = true;
            BuildRig(); var tile = Tile("WishTile"); var pulses = Pulses();
            AcceptedCast(5000); Frame();
            Assert.That(Glow.enabled, Is.True, "the wash stays on");
            Assert.That(Glow.color.a, Is.GreaterThan(0f), "and is visible");
            Assert.That(WedgeFill(tile), Is.EqualTo(.5f).Within(.01f), "set-up: half the recharge is left");
            _local += 100; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.49f).Within(.004f), "the wedge still moves");
            Assert.That(pulses.Count, Is.EqualTo(0), "nothing pulses");
            Assert.That(tile.localScale.x, Is.EqualTo(1f), "the Wish tile is at scale 1");
            double start = _local;
            Call(_hud, "InputCue", WireCommand.Cast(0, 0f, 0f));
            for (int ms = 10; ms <= 150; ms += 10)
            {
                _local = start + ms; Frame();
                Assert.That(tile.localScale.x, Is.EqualTo(1f), "the scale is exactly 1 at +" + ms + " ms, press included");
                Assert.That(pulses.Count, Is.EqualTo(0), "still no pulse at +" + ms + " ms");
            }
            Assert.That(Glow.enabled, Is.True, "the wash is still on");
        }

        // ------------------------------------------------------------------ 12. one owner of pulses

        [Test]
        public void EveryPressPulseGoesThroughTheOneHudPulses()
        {
            BuildRig(); Snap(10, 50000); Frame();
            var field = typeof(MatchHud).GetField("_pulses", All);
            Assert.That(field, Is.Not.Null, "MatchHud keeps _pulses");
            Assert.That(field.FieldType, Is.EqualTo(typeof(HudPulses)), "and it is the one HudPulses");
            foreach (var f in typeof(MatchHud).GetFields(All))
            {
                bool tuples = f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                    && f.FieldType.GetGenericArguments()[0] == typeof(ValueTuple<RectTransform, float>);
                Assert.That(tuples, Is.False, "field " + f.Name + " is not a second list of pulses");
            }
            var seconds = typeof(MatchHud).GetField("PulseSeconds", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var pop = typeof(MatchHud).GetField("PulsePop", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(seconds, Is.Not.Null, "MatchHud.PulseSeconds stays");
            Assert.That(pop, Is.Not.Null, "MatchHud.PulsePop stays");
            Assert.That((float)seconds.GetRawConstantValue(), Is.EqualTo(HudPulses.Seconds), "PulseSeconds is HudPulses.Seconds");
            Assert.That((float)pop.GetRawConstantValue(), Is.EqualTo(HudPulses.Pop), "PulsePop is HudPulses.Pop");

            var pulses = Pulses();
            var cues = new[] { WireCommand.Cast(0, 0f, 0f), WireCommand.Cast(1, 0f, 0f), WireCommand.UseConsumable(), WireCommand.Swap(0), WireCommand.Pickup(new PickupHandle(5)) };
            UiMotion.ReducedOverride = true;
            foreach (var cue in cues) Call(_hud, "InputCue", cue);
            Assert.That(pulses.Count, Is.EqualTo(0), "with reduced motion on no cue pulses");
            UiMotion.ReducedOverride = false;
            foreach (var cue in cues) Call(_hud, "InputCue", cue);
            Assert.That(pulses.Count, Is.EqualTo(5), "with it off every cue pulses its own element");
            _local += 160; Call(_hud, "TickActionRow");
            Assert.That(pulses.Count, Is.EqualTo(0), "and the row's tick ends them all");
        }

        // ------------------------------------------------------------------ 13. no garbage

        [Test]
        public void ASteadyActionRowTickAllocatesNothing()
        {
            BuildRig();
            SnapWish(10, 50000, Wish(0, 55000, 10000, 50000, 51000), 400);
            Items(10, Inventory(0));
            Items(11, Spells(new WireQuestSpell { Slot = 0, Reward = 3, Charges = 2, ReadyAt = 55000 }, new WireQuestSpell { Slot = 1, Reward = 4, Charges = 1, ReadyAt = 56000 }));
            Frame();
            Assert.That(WedgeOn(Tile("WishTile")), Is.True, "set-up: the Wish wedge is on");
            Assert.That(WedgeOn(Tile("QuestSpell1")), Is.True, "set-up: the Shift wedge is on");
            Assert.That(WedgeOn(Tile("QuestSpell2")), Is.True, "set-up: the Q wedge is on");
            Assert.That(CastOn(Tile("WishTile")), Is.True, "set-up: the cast bar is on");
            Assert.That(AttackOn(Tile("Slot1")), Is.True, "set-up: the attack bar is on");
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickActionRow", All));
            AllocationMeter.AssertNone(tick, 20, "a steady action-row tick");
        }

        // ------------------------------------------------------------------ 14. a new session

        [Test]
        public void ANewSessionRestartsTheClock()
        {
            BuildRig(); var tile = Tile("WishTile");
            SnapWish(10, 50000, Wish(0, 55000, 10000)); Frame();
            _local += 150; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.485f).Within(.003f), "set-up: the first session's clock ran 150 ms ahead");

            var transport = new Transport();
            var second = new Session(transport, new SessionOptions { Url = "ws://motion-two/ws", Profile = "motion-two", Now = () => 1000 });
            second.Connect(); transport.Open(); Hello(transport);
            transport.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 50100, Phase = (byte)MatchPhase.Active, Me = Self(Wish(0, 55100, 10000), 0) });
            _hud.Bind(second);
            Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.5f).Within(.002f), "the new session's first frame draws its own time, not the old clock's 50150");
            Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.5f).Within(.002f), "and the next frame at the same local time too");
            _local += 100; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.49f).Within(.003f), "then it moves");
        }

        // ------------------------------------------------------------------ 15. a dirty frame

        [Test]
        public void ADirtyFrameWithoutASnapshotKeepsTheFill()
        {
            BuildRig(); var tile = Tile("WishTile");
            SnapWish(10, 50000, Wish(0, 55000, 10000, 50000, 51000)); Frame();
            _local += 100; Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.49f).Within(.004f), "set-up: the wedge has moved");
            Assert.That(CastFill(tile), Is.EqualTo(.1f).Within(.01f), "set-up: the cast bar has moved");
            SetF(_hud, "_dirty", true);
            Frame();
            Assert.That(WedgeFill(tile), Is.EqualTo(.49f).Within(.004f), "a dirty frame keeps the wedge where the clock put it, not the snapshot's .5");
            Assert.That(CastFill(tile), Is.EqualTo(.1f).Within(.01f), "and the cast bar, not the snapshot's 0");
        }
    }
}
