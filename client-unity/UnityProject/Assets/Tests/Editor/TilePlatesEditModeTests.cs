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
    /// P-UI-TILE-PLATES-1: the six elements of the action row (Slot1, QSwap, Slot2, WishTile, QuestSpell1, QuestSpell2) each stand on one
    /// solid SurfaceRaised plate in every state (the held weapon slot on SurfaceSelected); an empty one is told apart by its content (a
    /// dim key cap and dim name text), never by a see-through fill or a CanvasGroup fade; an empty tile is not a Tab stop yet still
    /// hints on hover; nothing here is rewritten in a steady frame. One whole MatchHud on a real session over a fake transport, as
    /// AbilityRowEditModeTests builds it. Nothing reads a clock: HudLocalMs is a fixed seam and the server time is the delivered one.
    /// </summary>
    public sealed class TilePlatesEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string[] Elements = { "Slot1", "QSwap", "Slot2", "WishTile", "QuestSpell1", "QuestSpell2" };

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
        uint _tick = 10;
        double _ms = 1000;

        void BuildFullRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Tile plates events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Tile plates fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://plates/ws", Profile = "plates", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _hud.HudLocalMs = () => 5000d;   // EditMode time does not advance: Frame and TickActionRow read this seam
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(1, 50);
        }

        void Snap(uint tick, double serverMs, byte phase = (byte)MatchPhase.Active) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        void Items(uint tick, params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = tick, Items = items });

        /// <summary>One frame of the HUD (it redraws the tiles from the delivered facts: call it before a state's Show calls, never after).</summary>
        void Frame() { Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        [TearDown]
        public void TearDown()
        {
            HudIconHint.CloseAll();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------ helpers

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

        RectTransform FullBar => Child((RectTransform)GetF(_hud, "_stage"), "PlayerHUD/CombatActions");
        RectTransform Element(string name) => Child(FullBar, name);
        AbilityTile WishTile => (AbilityTile)GetF(_hud, "_wishAbilityTile");
        AbilityTile[] QuestTiles => (AbilityTile[])GetF(_hud, "_questTiles");
        AbilityTile TileAt(int ability) => ability == 0 ? WishTile : QuestTiles[ability - 1];   // 0 = R, 1 = Shift, 2 = Q
        TMP_Text SlotName(int i) => ((TMP_Text[])GetF(_hud, "_slotNames"))[i];
        Button SlotButton(int i) => ((Button[])GetF(_hud, "_slots"))[i];

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

        /// <summary>The colour the element's plate is drawn in: Image.color, times the ColorTint Button's normalColor and colorMultiplier,
        /// times every CanvasGroup alpha from the image up to and including CombatActions.</summary>
        Color Drawn(Transform element)
        {
            var image = element.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, element.name + " has a plate Image");
            Color c = image.color;
            var button = element.GetComponent<Button>();
            if (button != null && button.transition == Selectable.Transition.ColorTint) c *= button.colors.normalColor * button.colors.colorMultiplier;
            return c * GroupAlpha(image.transform);
        }

        /// <summary>The colour the element's frame ring is drawn in (the child Frame, with the group alphas).</summary>
        Color FrameDrawn(Transform element)
        {
            var ring = Child(element, "Frame").GetComponent<Image>();
            return ring.color * GroupAlpha(ring.transform);
        }

        float GroupAlpha(Transform from)
        {
            float alpha = 1f;
            var bar = FullBar;
            for (var t = from; t != null; t = t.parent)
            {
                foreach (var group in t.GetComponents<CanvasGroup>()) alpha *= group.alpha;
                if (t == bar) break;
            }
            return alpha;
        }

        static void AssertRgb(Color actual, Color32 expected, string what)
        {
            var a = (Color32)actual;
            Assert.That(new[] { (int)a.r, a.g, a.b }, Is.EqualTo(new[] { (int)expected.r, expected.g, expected.b }),
                what + " has the rgb of " + expected + " but is " + a);
        }

        static void AssertAlpha(Color actual, float expected, string what) =>
            Assert.That(actual.a, Is.EqualTo(expected).Within(.001f), what + " alpha");

        static double Linear(double channel) => channel <= .03928 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        static double Luminance(double r, double g, double b) => .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);

        /// <summary>WCAG contrast of a (possibly translucent) colour over an opaque plate; the text is blended over the plate in sRGB first.</summary>
        static double Ratio(Color text, Color32 plate)
        {
            double a = text.a, pr = plate.r / 255.0, pg = plate.g / 255.0, pb = plate.b / 255.0;
            double r = text.r * a + pr * (1 - a), g = text.g * a + pg * (1 - a), b = text.b * a + pb * (1 - a);
            double lt = Luminance(r, g, b), lp = Luminance(pr, pg, pb);
            double hi = Math.Max(lt, lp), lo = Math.Min(lt, lp);
            return (hi + .05) / (lo + .05);
        }

        // ------------------------------------------------------------------ the states a row walks through

        sealed class State
        {
            public string Name;
            public bool[] Has = new bool[2];
            public int Held = -1;   // the slot index in hand with a weapon in it, else -1
            public AbilityPhase[] Phases = { AbilityPhase.Empty, AbilityPhase.Empty, AbilityPhase.Empty };   // R, Shift, Q
        }

        static readonly AbilityFace Ready = new AbilityFace(AbilityPhase.Ready, 0f, 0, 2, -1f);
        static readonly AbilityFace Cooling = new AbilityFace(AbilityPhase.Cooling, .5f, 3, 2, -1f);

        static InventoryState Inventory(byte active, WeaponId? slot0, WeaponId? slot1) => new InventoryState { Value = new WireInventory
        {
            ActiveSlot = active, PotionSmall = 1,
            Slot0 = slot0.HasValue ? (byte?)(byte)slot0.Value : null, Slot1 = slot1.HasValue ? (byte?)(byte)slot1.Value : null,
        } };

        /// <summary>Delivers an inventory and runs the frame that draws it, then proves through the session that it arrived.</summary>
        void DeliverInventory(InventoryState inventory, byte active, bool slot0, bool slot1)
        {
            Snap(_tick, _ms += 50); Items(_tick++, inventory); Frame();
            var view = _session.Collections.Inventory;
            Assert.That(view.HasValue, Is.True, "set-up: the inventory was delivered");
            Assert.That(view.Value.Slot0.HasValue, Is.EqualTo(slot0), "set-up: slot 0");
            Assert.That(view.Value.Slot1.HasValue, Is.EqualTo(slot1), "set-up: slot 1");
            Assert.That(view.Value.ActiveSlot, Is.EqualTo(active), "set-up: the slot in hand");
        }

        void ShowAll(AbilityFace wish, AbilityFace one, AbilityFace two)
        {
            WishTile.Show(wish, wish.Phase == AbilityPhase.Empty ? AbilityFaces.NoWishLabel : "Blaze");
            QuestTiles[0].Show(one, one.Phase == AbilityPhase.Empty ? "" : "Hook");
            QuestTiles[1].Show(two, two.Phase == AbilityPhase.Empty ? "" : "Hook");
        }

        /// <summary>Builds one rig and walks the six states of the spec, calling <paramref name="check"/> after each.</summary>
        void Walk(Action<State> check)
        {
            BuildFullRig();
            Frame();
            Assert.That(FullBar.gameObject.activeInHierarchy, Is.True, "set-up: the action row is visible in an active match");

            // (a) as built: no inventory, all three tiles Empty.
            check(new State { Name = "a: as built" });

            // (b) holds nothing.
            DeliverInventory(Inventory(0, null, null), 0, false, false);
            check(new State { Name = "b: holds nothing" });

            // (c) a Sword in slot 0, slot 1 empty, slot 1 in hand (no held slot).
            DeliverInventory(Inventory(1, WeaponId.Sword, null), 1, true, false);
            check(new State { Name = "c: sword, empty slot in hand", Has = { [0] = true } });

            // (d) Sword and Bow, slot 0 in hand; Wish Ready, Shift Ready, Q Cooling.
            DeliverInventory(Inventory(0, WeaponId.Sword, WeaponId.Bow), 0, true, true);
            Assert.That(SlotButton(0).colors.normalColor, Is.EqualTo((Color)UiTheme.Current.SurfaceSelected), "set-up: the held slot refreshed");
            ShowAll(Ready, Ready, Cooling);
            check(new State { Name = "d: slot 0 held, R and Shift ready, Q cooling", Has = { [0] = true, [1] = true }, Held = 0,
                Phases = { [0] = AbilityPhase.Ready, [1] = AbilityPhase.Ready, [2] = AbilityPhase.Cooling } });

            // (e) Sword and Bow, slot 1 in hand; Wish Cooling, Shift Cooling, Q Ready.
            DeliverInventory(Inventory(1, WeaponId.Sword, WeaponId.Bow), 1, true, true);
            Assert.That(SlotButton(1).colors.normalColor, Is.EqualTo((Color)UiTheme.Current.SurfaceSelected), "set-up: the held slot moved");
            ShowAll(Cooling, Cooling, Ready);
            check(new State { Name = "e: slot 1 held, R and Shift cooling, Q ready", Has = { [0] = true, [1] = true }, Held = 1,
                Phases = { [0] = AbilityPhase.Cooling, [1] = AbilityPhase.Cooling, [2] = AbilityPhase.Ready } });

            // (f) the inventory is gone (a rebind) and every tile is Empty again.
            Call(_hud, "RefreshBuildIcons", new CollectionsView());
            ShowAll(AbilityFace.Empty, AbilityFace.Empty, AbilityFace.Empty);
            check(new State { Name = "f: no inventory, all tiles empty again" });
        }

        // ------------------------------------------------------------------ 1. one plate

        [Test]
        public void EverySlotAndTileStandsOnOnePlateInEveryState()
        {
            var theme = UiTheme.Current;
            Walk(state =>
            {
                for (int i = 0; i < Elements.Length; i++)
                {
                    string what = state.Name + " / " + Elements[i];
                    var element = Element(Elements[i]);
                    bool held = i == 0 && state.Held == 0 || i == 2 && state.Held == 1;
                    var plate = Drawn(element);
                    AssertAlpha(plate, 1f, what + " plate");
                    AssertRgb(plate, held ? theme.SurfaceSelected : theme.SurfaceRaised, what + " plate");
                    bool ready = i >= 3 && state.Phases[i - 3] == AbilityPhase.Ready;
                    var frame = FrameDrawn(element);
                    AssertAlpha(frame, 1f, what + " frame");
                    AssertRgb(frame, ready ? theme.BorderStrong : theme.Border, what + " frame");
                }
            });
        }

        // ------------------------------------------------------------------ 2. told apart by content only

        [Test]
        public void AnEmptyTileIsToldApartByItsContentOnly()
        {
            var theme = UiTheme.Current;
            Assert.That(AbilityTile.EmptyKeyAlpha, Is.EqualTo(.7f).Within(.001f), "the empty key cap alpha");
            Assert.That(AbilityTile.EmptyTextAlpha, Is.EqualTo(.8f).Within(.001f), "the empty text alpha");
            var dim = AbilityTile.EmptyTextColour(theme);
            AssertRgb(dim, theme.TextSecondary, "EmptyTextColour");
            AssertAlpha(dim, .8f, "EmptyTextColour");

            Color emptyKey = default, emptySlotText = default, filledSlotText = default;
            Walk(state =>
            {
                for (int a = 0; a < 3; a++)
                {
                    string what = state.Name + " / " + (a == 0 ? "R" : a == 1 ? "Shift" : "Q");
                    var tile = TileAt(a); bool empty = state.Phases[a] == AbilityPhase.Empty;
                    AssertRgb(tile.KeyLabel.color, theme.TextSecondary, what + " key cap");
                    AssertAlpha(tile.KeyLabel.color, empty ? .7f : 1f, what + " key cap");
                    AssertRgb(tile.NameLabel.color, empty ? theme.TextSecondary : theme.TextPrimary, what + " name");
                    AssertAlpha(tile.NameLabel.color, empty ? .8f : 1f, what + " name");
                    if (state.Name.StartsWith("a:") && a == 1) emptyKey = tile.KeyLabel.color;
                }
                for (int i = 0; i < 2; i++)
                {
                    string what = state.Name + " / slot " + (i + 1) + " name";
                    var colour = SlotName(i).color;
                    AssertRgb(colour, state.Has[i] ? theme.TextPrimary : theme.TextSecondary, what);
                    AssertAlpha(colour, state.Has[i] ? 1f : .8f, what);
                    if (state.Name.StartsWith("c:")) { if (i == 0) filledSlotText = colour; else emptySlotText = colour; }
                }
            });

            Assert.That(Ratio(emptyKey, theme.SurfaceRaised), Is.GreaterThanOrEqualTo(4.5), "the empty key cap reads 4.5:1 on the plate");
            Assert.That(Ratio(emptyKey, theme.SurfaceRaised), Is.LessThan(Ratio(theme.TextSecondary, theme.SurfaceRaised)), "and below the filled key cap");
            Assert.That(Ratio(emptySlotText, theme.SurfaceRaised), Is.GreaterThanOrEqualTo(4.5), "the empty slot text reads 4.5:1 on the plate");
            Assert.That(Ratio(emptySlotText, theme.SurfaceHover), Is.GreaterThanOrEqualTo(4.5), "and on a hovered plate");
            Assert.That(Ratio(emptySlotText, theme.SurfaceRaised), Is.LessThan(Ratio(filledSlotText, theme.SurfaceRaised)), "below the filled slot's text");
            Assert.That(Ratio(theme.Border, theme.SurfaceRaised), Is.GreaterThanOrEqualTo(3.0), "the frame against the plate");
        }

        // ------------------------------------------------------------------ 3. no fade

        [Test]
        public void NoCanvasGroupFadesTheRow()
        {
            Walk(state =>
            {
                var bar = FullBar;
                foreach (var group in bar.GetComponentsInChildren<CanvasGroup>(true))
                    Assert.That(group.alpha, Is.GreaterThanOrEqualTo(1f), state.Name + ": the group on " + group.transform.name + " fades the row");
                foreach (string slot in new[] { "Slot1", "Slot2" })
                    Assert.That(Element(slot).GetComponentsInChildren<CanvasGroup>(true).Length, Is.EqualTo(0), state.Name + ": " + slot + " carries a CanvasGroup");
            });
        }

        // ------------------------------------------------------------------ 4. an empty tile is not a control and still hints

        [Test]
        public void AnEmptyAbilityTileIsNotAControlAndStillHints()
        {
            BuildFullRig(); Frame();
            var bar = FullBar;
            Assert.That(bar.gameObject.activeInHierarchy, Is.True, "set-up: the row is visible");
            var tiles = new[] { WishTile, QuestTiles[0], QuestTiles[1] };
            foreach (var tile in tiles) Assert.That(tile.Button.interactable, Is.False, tile.Root.name + " is not interactable while empty");

            try
            {
                // Hover first (a selection also shows a hint): an empty tile hints as a filled one does.
                var tile1 = QuestTiles[0];
                Assert.That(tile1.Hint.Text, Is.Not.Empty, "set-up: the empty tile has a hint");
                Hover(tile1, true);
                Assert.That(tile1.Hint.IsVisible, Is.True, "hovering an empty tile shows its hint");
                Hover(tile1, false);
                Assert.That(tile1.Hint.IsVisible, Is.False, "leaving it hides the hint");
                tile1.Show(Ready, "Hook", "Shift · Hook\n2 charges · Ready");
                Assert.That(tile1.Hint.Text, Is.Not.Empty, "set-up: the filled tile has a hint");
                Hover(tile1, true);
                Assert.That(tile1.Hint.IsVisible, Is.True, "hovering a filled tile shows its hint");
                Hover(tile1, false);
                Assert.That(tile1.Hint.IsVisible, Is.False, "and leaving hides it");
                tile1.Show(AbilityFace.Empty, "", "Shift · Quest spell");
                HudIconHint.CloseAll();

                // Tab: 16 steps never rest on an empty tile.
                var system = EventSystem.current;
                var empties = new HashSet<GameObject>(); foreach (var tile in tiles) empties.Add(tile.Root.gameObject);
                for (int i = 0; i < 16; i++)
                {
                    Assert.That(UiFocusNavigation.Cycle(bar, false), Is.True, "set-up: step " + i + " found a control in the row");
                    Assert.That(empties.Contains(system.currentSelectedGameObject), Is.False, "step " + i + " selected an empty tile");
                }
                system.SetSelectedGameObject(null);

                // A filled tile is a Tab stop.
                tile1.Show(Ready, "Hook");
                Assert.That(tile1.Button.interactable, Is.True, "a Ready tile is interactable");
                bool reached = false;
                for (int i = 0; i < 16; i++)
                {
                    UiFocusNavigation.Cycle(bar, false);
                    if (system.currentSelectedGameObject == tile1.Root.gameObject) reached = true;
                }
                Assert.That(reached, Is.True, "16 steps reach the filled tile");
            }
            finally
            {
                HudIconHint.CloseAll();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
        }

        static void Hover(AbilityTile tile, bool enter)
        {
            var data = new PointerEventData(EventSystem.current);
            if (enter) ExecuteEvents.Execute(tile.Root.gameObject, data, ExecuteEvents.pointerEnterHandler);
            else ExecuteEvents.Execute(tile.Root.gameObject, data, ExecuteEvents.pointerExitHandler);
        }

        // ------------------------------------------------------------------ 5. no garbage

        [Test]
        public void AnUnchangedFaceAllocatesNothingInEveryPhase()
        {
            BuildFullRig();
            var tile = new AbilityTile((RectTransform)GetF(_hud, "_stage"), "PlateTile", HudRegions.AbilityElement(4), 34f, "Shift",
                (RectTransform)GetF(_hud, "_root"));
            foreach (var face in new[] { AbilityFace.Empty, Ready, Cooling })
            {
                Action show = () => tile.Show(face, "Hook", "Shift · Hook\n2 charges");
                show();
                Assert.That(tile.Button.interactable, Is.EqualTo(face.Phase != AbilityPhase.Empty), "set-up: the " + face.Phase + " face is drawn");
                for (int i = 0; i < 5; i++) show();   // warm up
                AllocationMeter.AssertNone(show, 200, "200 unchanged Show calls of a " + face.Phase + " face");
            }
        }

        // ------------------------------------------------------------------ 6. a frame does not rewrite the look

        [Test]
        public void AFrameDoesNotRewriteTheRowsLook()
        {
            BuildFullRig(); Frame();
            var theme = UiTheme.Current;
            var key = QuestTiles[0].KeyLabel; var slotName = SlotName(0);
            key.color = Color.magenta; slotName.color = Color.magenta;
            for (int i = 0; i < 3; i++) { Frame(); Call(_hud, "TickActionRow"); }
            Assert.That(key.color, Is.EqualTo(Color.magenta), "a frame leaves the key cap alone");
            Assert.That(slotName.color, Is.EqualTo(Color.magenta), "a frame leaves the slot name alone");

            QuestTiles[0].Show(Ready, "Hook");
            AssertRgb(key.color, theme.TextSecondary, "after Show(Ready) the key cap");
            AssertAlpha(key.color, 1f, "after Show(Ready) the key cap");
            Call(_hud, "RefreshBuildIcons", new CollectionsView());
            AssertRgb(slotName.color, theme.TextSecondary, "after RefreshBuildIcons the empty slot name");
            AssertAlpha(slotName.color, AbilityTile.EmptyTextAlpha, "after RefreshBuildIcons the empty slot name");
            Assert.That(slotName.color, Is.EqualTo(AbilityTile.EmptyTextColour(theme)), "it is EmptyTextColour");
        }
    }
}
