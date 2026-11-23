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
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WISH-TILE-1: the Wish tile (key R) is an AbilityTile like Shift and Q. A bare MatchHud with CreateCombatHud and no session;
    /// RefreshOwnerPresentation delivers a SelfView with a WishView and its argument is the server time, so nothing here reads a clock
    /// (EditMode time does not advance). The tile is WishTile under HudStage/PlayerHUD/CombatActions.
    /// </summary>
    public sealed class WishTileEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        GameObject _root;
        MatchHud _hud;
        ShowcaseSettings _settings;
        UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _root = new GameObject("Wish tile test", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            var hudObject = new GameObject("MatchHud", typeof(RectTransform));
            hudObject.transform.SetParent(_root.transform, false);
            var hudRect = (RectTransform)hudObject.transform;
            hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one;
            hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;
            _hud = hudObject.AddComponent<MatchHud>();
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settings = _root.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetField("_textScale", All).SetValue(_settings, 1f);
            }
            SetField("_root", _root.GetComponent<RectTransform>());
            SetField("_hud", hudRect);
            SetField("_showcaseSettings", _settings);
            Invoke("CreateCombatHud");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            _root = null; _hud = null; _settings = null;
        }

        // ------------------------------------------------------------------ helpers

        object GetField(string name) { var f = typeof(MatchHud).GetField(name, All); return f == null ? null : f.GetValue(_hud); }
        void SetField(string name, object value) => typeof(MatchHud).GetField(name, All).SetValue(_hud, value);
        void Invoke(string name, params object[] args) => typeof(MatchHud).GetMethod(name, All).Invoke(_hud, args);

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
        }

        RectTransform Tile => Child(_root.transform, "HudStage/PlayerHUD/CombatActions/WishTile");
        RectTransform QuestTile(int i) => ((AbilityTile[])GetField("_questTiles"))[i].Root;
        AbilityTile WishAbilityTile => GetField("_wishAbilityTile") as AbilityTile;
        HudIconHint WishHint => (HudIconHint)GetField("_wishHint");

        static string Text(Transform tile, string box) => Child(tile, box + "/Label").GetComponent<TMP_Text>().text;
        static bool NameBoxOn(Transform tile) => Child(tile, "Name").gameObject.activeSelf;
        static bool WedgeOn(Transform tile) { var s = Child(tile, "CooldownSweep").GetComponent<Image>(); return s.enabled && s.gameObject.activeSelf; }
        static bool CastOn(Transform tile) { var s = Child(tile, "Cast").GetComponent<Image>(); return s.enabled && s.gameObject.activeSelf; }
        static Image Surface(Transform tile) => tile.GetComponent<Image>();
        static Color FrameColour(Transform tile) => Child(tile, "Frame").GetComponent<Image>().color;

        static WishView Wish(AbilityId ability, byte charges, byte max, double readyAt, double? fullAt = null, double? nextAt = null,
            double recharge = 10000d, WishCastView? cast = null) =>
            new WishView { Ability = ability, Charges = charges, MaxCharges = max, ReadyAt = readyAt, FullAt = fullAt, NextChargeAt = nextAt, RechargeMs = recharge, Cast = cast };

        static SelfView Me(WishView? wish) => new SelfView { Hp = 90, MaxHp = 100, Status = ActorStatus.Alive, Wish = wish };

        void Deliver(WishView? wish, double serverMs) => Invoke("RefreshOwnerPresentation", Me(wish), serverMs);

        // ------------------------------------------------------------------ 1. the name once, the countdown once

        [Test]
        public void AReadyWishShowsItsNameAndARechargingWishItsCountdownOnce()
        {
            var tile = Tile;
            Deliver(Wish(AbilityId.Blaze, 1, 2, 4000, 14000, 14000), 5000);
            Assert.That(Text(tile, "Name"), Is.EqualTo(HudContentText.WishName(null, AbilityId.Blaze)), "a ready Wish shows its name");
            Assert.That(NameBoxOn(tile), Is.True, "the name box is on");
            Assert.That(Text(tile, "Seconds"), Is.EqualTo(""), "no countdown for the charge still recharging");
            Assert.That(Text(tile, "Charges"), Is.EqualTo("1"), "the stock");
            Assert.That(WedgeOn(tile), Is.False, "no wedge");
            StringAssert.Contains("Blaze", WishHint.Text);
            StringAssert.Contains("Next charge in 9", WishHint.Text);

            Deliver(Wish(AbilityId.Blaze, 0, 2, 14000, 24000, 14000), 5000);
            Assert.That(Text(tile, "Seconds"), Is.EqualTo("9"), "the whole seconds until ReadyAt");
            Assert.That(Text(tile, "Charges"), Is.EqualTo("0"), "the stock is 0");
            Assert.That(NameBoxOn(tile), Is.False, "the name is hidden while it recharges");
            Assert.That(WedgeOn(tile), Is.True, "the wedge is on");
            var others = tile.GetComponentsInChildren<TMP_Text>(true).Where(t => t.transform.parent.name != "Seconds" && t.text.Contains("9")).Select(t => t.transform.parent.name + "/" + t.name + " '" + t.text + "'").ToList();
            Assert.That(others, Is.Empty, "the countdown is on the tile once: " + string.Join(", ", others));
            Assert.That(WishAbilityTile?.ReadyAtMs, Is.EqualTo(14000d), "the tile keeps ReadyAt");
            Assert.That(WishAbilityTile?.TotalMs, Is.EqualTo(10000d), "and RechargeMs");

            Deliver(null, 5010);
            Assert.That(WishAbilityTile?.ReadyAtMs, Is.EqualTo(0d), "no Wish: 0");
            Assert.That(WishAbilityTile?.TotalMs, Is.EqualTo(0d), "and 0");
        }

        // ------------------------------------------------------------------ 2. the badge

        [Test]
        public void OneChargeShowsNoBadgeAndMoreShowTheirStock()
        {
            var tile = Tile;
            Deliver(Wish(AbilityId.Blaze, 1, 1, 0), 5000);
            Assert.That(Text(tile, "Charges"), Is.EqualTo(""), "max 1: no badge");
            Assert.That(Text(tile, "Name"), Is.EqualTo(HudContentText.WishName(null, AbilityId.Blaze)), "ready: the name");
            Deliver(Wish(AbilityId.Blaze, 0, 2, 9000, 19000, 9000), 5100);
            Assert.That(Text(tile, "Charges"), Is.EqualTo("0"), "max 2, none left, recharging");
            Deliver(Wish(AbilityId.LastStand, 3, 3, 0), 5200);
            Assert.That(Text(tile, "Charges"), Is.EqualTo("3"), "max 3, all three");
        }

        // ------------------------------------------------------------------ 3. the longest name fits

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
        public void TheLongestWishNameFits(float textScale)
        {
            var wall = new WireHudContent { Terms = new[] { new WireHudTerm { Code = (byte)HudTerm.FrostWish, Value = 1 } } };
            Assert.That(wall.FrostWish, Is.EqualTo(FrostWishKind.Wall), "set-up: the block says wall");
            var cases = new List<(AbilityId, WireHudContent)>();
            foreach (AbilityId id in Enum.GetValues(typeof(AbilityId))) cases.Add((id, null));
            cases.Add((AbilityId.Freeze, wall));
            var tile = Tile;
            MythicUi.ApplyTextScale(_root.transform, textScale);
            var problems = new List<string>();
            double ms = 5000;
            foreach (var (ability, block) in cases)
            {
                _hud.HudContent = block;
                string expected = HudContentText.WishName(block, ability);
                Deliver(Wish(ability, 1, 1, 0), ms += 100);
                var label = Child(tile, "Name/Label").GetComponent<TMP_Text>();
                if (label.text != expected) problems.Add("the label reads '" + label.text + "', not '" + expected + "'");
                var follow = label.GetComponent<MythicTextScale>();
                if (follow == null) { problems.Add("Name has no MythicTextScale"); continue; }
                follow.Refit();
                if (label.overflowMode == TextOverflowModes.Ellipsis || label.overflowMode == TextOverflowModes.Truncate)
                    problems.Add("'" + expected + "' would be cut (overflow " + label.overflowMode + ")");
                float size = label.fontSize; var rect = label.rectTransform.rect; var need = Needs(label, label.text, size);
                if (size < 8f) problems.Add("'" + expected + "' fits only at size " + size + " at text scale " + textScale);
                if (need.x > rect.width * MythicTextScale.WidthShare + .001f || need.y > rect.height + .01f)
                    problems.Add("'" + expected + "' at " + size + " needs " + need.x.ToString("0.#") + "x" + need.y.ToString("0.#") + " of " + rect.width.ToString("0.#") + "x" + rect.height.ToString("0.#"));
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ 4. no Wish

        void AssertNoWishLook(Transform tile, string when)
        {
            Assert.That(Text(tile, "Name"), Is.EqualTo("—"), when + ": the name reads a dash");
            Assert.That(Text(tile, "Seconds"), Is.EqualTo(""), when + ": no seconds");
            Assert.That(Text(tile, "Charges"), Is.EqualTo(""), when + ": no badge");
            Assert.That(WedgeOn(tile), Is.False, when + ": no wedge");
            Assert.That(CastOn(tile), Is.False, when + ": no cast bar");
            Assert.That(Surface(tile).color.a, Is.LessThan(.5f), when + ": the surface is dim");
            Assert.That(tile.GetComponent<Button>().interactable, Is.False, when + ": the button is not interactable");
        }

        [Test]
        public void NoWishShowsADash()
        {
            var tile = Tile;
            AssertNoWishLook(tile, "a fresh tile");
            var cast = new WishCastView { Ability = AbilityId.Blaze, StartedAt = 4500, LandsAt = 6500 };
            Deliver(Wish(AbilityId.Blaze, 0, 2, 14000, 24000, 14000, 10000d, cast), 5000);
            Assert.That(Text(tile, "Seconds"), Is.EqualTo("9"), "set-up: a recharging Wish with a cast in flight is drawn");
            Deliver(null, 5100);
            AssertNoWishLook(tile, "after a Wish, then none delivered");
            Deliver(Wish(AbilityId.Blaze, 0, 2, 14000, 24000, 14000, 10000d, cast), 5200);
            Invoke("ClearOwnerPresentation", "Awaiting server");
            AssertNoWishLook(tile, "after a Wish, then ClearOwnerPresentation");
        }

        // ------------------------------------------------------------------ 5. one tile class, the same boxes

        static readonly string[] SharedBoxes = { "Key", "Name", "Seconds", "CooldownSweep", "Charges", "Cast" };

        /// <summary>A child's rect in the tile's space, in HUD units from the tile's top-left corner.</summary>
        static Rect InTile(RectTransform tile, RectTransform box)
        {
            var corners = new Vector3[4]; box.GetWorldCorners(corners);
            Vector3 lo = tile.InverseTransformPoint(corners[0]), hi = tile.InverseTransformPoint(corners[2]);
            var r = tile.rect;
            return new Rect(lo.x - r.xMin, r.yMax - hi.y, hi.x - lo.x, hi.y - lo.y);
        }

        static bool Overlap(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > .01f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > .01f;

        [Test]
        public void TheWishTileAndTheQuestTilesAreOneClass()
        {
            var at = WishAbilityTile;
            Assert.That(at, Is.Not.Null, "_wishAbilityTile is an AbilityTile");
            Assert.That(at?.Root.name, Is.EqualTo("WishTile"));
            Assert.That(at?.Root, Is.SameAs(GetField("_wishTile")), "_wishTile is its root");
            var tile = Tile;
            var tiles = new[] { QuestTile(0), QuestTile(1), tile };
            foreach (var other in tiles) Assert.That(other.rect.size.x, Is.EqualTo(64f).Within(.01f), other.name + " is 64 wide");
            foreach (string box in SharedBoxes)
            {
                var first = Child(tiles[0], box);
                for (int i = 1; i < tiles.Length; i++)
                {
                    var other = Child(tiles[i], box);
                    string what = tiles[i].name + "/" + box + " against QuestSpell1/" + box;
                    Assert.That(other.anchorMin, Is.EqualTo(first.anchorMin), what + " anchorMin");
                    Assert.That(other.anchorMax, Is.EqualTo(first.anchorMax), what + " anchorMax");
                    Assert.That(other.pivot, Is.EqualTo(first.pivot), what + " pivot");
                    Assert.That(other.anchoredPosition, Is.EqualTo(first.anchoredPosition), what + " anchoredPosition");
                    Assert.That(other.sizeDelta, Is.EqualTo(first.sizeDelta), what + " sizeDelta");
                }
            }
            Assert.That(Text(tile, "Key"), Is.EqualTo("R"), "the key cap");

            var ready = new AbilityFace(AbilityPhase.Ready, 0f, 0, -1, -1f);
            var quest = (AbilityTile[])GetField("_questTiles");
            quest[0].Show(ready, "", ""); quest[1].Show(ready, "", "");
            Deliver(Wish(AbilityId.Blaze, 1, 1, 0), 5000);
            var theme = UiTheme.Current;
            Assert.That(Surface(tiles[1]).color, Is.EqualTo(Surface(tiles[0]).color), "QuestSpell2 surface");
            Assert.That(Surface(tile).color, Is.EqualTo(Surface(tiles[0]).color), "the Wish surface is the quest tiles' ready surface");
            Assert.That(FrameColour(tiles[0]), Is.EqualTo((Color)theme.BorderStrong), "QuestSpell1 frame");
            Assert.That(FrameColour(tiles[1]), Is.EqualTo((Color)theme.BorderStrong), "QuestSpell2 frame");
            Assert.That(FrameColour(tile), Is.EqualTo((Color)theme.BorderStrong), "the ready Wish frame is BorderStrong, not a faction colour");

            var boxes = new Dictionary<string, Rect>();
            foreach (string box in new[] { "Key", "Icon", "Name", "Charges", "Seconds" }) boxes[box] = InTile(tile, Child(tile, box));
            var whole = new Rect(0f, 0f, 64f, 64f);
            foreach (var pair in boxes)
                Assert.That(pair.Value.xMin >= -.01f && pair.Value.yMin >= -.01f && pair.Value.xMax <= 64.01f && pair.Value.yMax <= 64.01f, Is.True, pair.Key + " lies inside the tile: " + pair.Value);
            Assert.That(boxes["Icon"].width, Is.EqualTo(16f).Within(.01f), "the icon is 16 wide");
            Assert.That(boxes["Icon"].height, Is.EqualTo(16f).Within(.01f), "and 16 high");
            Assert.That(boxes["Icon"].xMax, Is.EqualTo(60f).Within(.01f), "inset 4 from the right");
            Assert.That(boxes["Icon"].yMin, Is.EqualTo(3f).Within(.01f), "inset 3 from the top");
            string[] four = { "Key", "Icon", "Name", "Charges" };
            for (int a = 0; a < four.Length; a++)
                for (int b = a + 1; b < four.Length; b++)
                    Assert.That(Overlap(boxes[four[a]], boxes[four[b]]), Is.False, four[a] + " and " + four[b] + " share no area: " + boxes[four[a]] + " " + boxes[four[b]]);
            foreach (string other in new[] { "Key", "Icon", "Charges" })
                Assert.That(Overlap(boxes["Seconds"], boxes[other]), Is.False, "Seconds and " + other + " share no area");

            var glow = Child(tile, "WishGlow");
            var glowImage = glow.GetComponent<Image>();
            Assert.That(glowImage.enabled, Is.False, "the glow is disabled");
            Assert.That(glow.GetSiblingIndex(), Is.LessThan(Child(tile, "Key").GetSiblingIndex()), "the glow is behind the labels");
        }

        // ------------------------------------------------------------------ 6. the cast bar

        [Test]
        public void TheCastBarFillsFromTheLeftWithTheCastProgress()
        {
            var tile = Tile;
            var cast = new WishCastView { Ability = AbilityId.Blaze, StartedAt = 4500, LandsAt = 6500 };
            Deliver(Wish(AbilityId.Blaze, 1, 2, 4000, 14000, 14000, 10000d, cast), 5000);
            var bar = Child(tile, "Cast").GetComponent<Image>();
            Assert.That(CastOn(tile), Is.True, "the cast bar is on while a cast is in flight");
            Assert.That(bar.sprite, Is.Not.Null, "it has a sprite: a Filled image with none draws its whole rect");
            Assert.That(bar.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(bar.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(bar.fillOrigin, Is.EqualTo(0), "it fills from the left");
            Assert.That(bar.fillAmount, Is.EqualTo(.25f).Within(.01f), "a quarter into the cast");
            Deliver(Wish(AbilityId.Blaze, 1, 2, 4000, 14000, 14000, 10000d, cast), 5500);
            Assert.That(bar.fillAmount, Is.EqualTo(.5f).Within(.01f), "half way");
            Deliver(Wish(AbilityId.Blaze, 1, 2, 4000, 14000, 14000), 5600);
            Assert.That(CastOn(tile), Is.False, "no cast, no bar");
            foreach (int i in new[] { 0, 1 }) Assert.That(CastOn(QuestTile(i)), Is.False, "the quest tile " + i + " has no cast bar on");
        }

        // ------------------------------------------------------------------ 6b. the badge follows the delivered stock

        /// <summary>
        /// P-UI-TILE-PLATES-1 (a standing guard, green on its base): elapsed time never predicts stock. Wave 3 removed the Wish summary label,
        /// whose line "elapsed time never predicts summary stock either" lived in MatchHudWishBarrierEditModeTests; this restores the fact
        /// for the tile's badge. The same view delivered again after NextChargeAt and FullAt have passed still reads one charge.
        /// </summary>
        [Test]
        public void TheBadgeFollowsTheDeliveredStockNotTheClock()
        {
            var tile = Tile;
            var wish = Wish(AbilityId.Blaze, 1, 2, 4000, 14000, 14000);
            Deliver(wish, 5000);
            Assert.That(Text(tile, "Charges"), Is.EqualTo("1"), "set-up: the delivered stock");
            Deliver(wish, 15000);
            Assert.That(Text(tile, "Charges"), Is.EqualTo("1"), "the same view at a later server time keeps the delivered stock");
            Assert.That(NameBoxOn(tile), Is.True, "and the name box is on");
        }

        // ------------------------------------------------------------------ 7. no garbage

        [Test]
        public void AnUnchangedWishRefreshAllocatesNothing()
        {
            var tile = Tile;
            var wish = Wish(AbilityId.Blaze, 0, 2, 14000, 24000, 14000, 10000d);
            Deliver(wish, 5000);
            var refresh = (Action<WishView?, double, UiTheme>)Delegate.CreateDelegate(typeof(Action<WishView?, double, UiTheme>), _hud, typeof(MatchHud).GetMethod("RefreshWishTile", All));
            WishView? arg = wish; var theme = UiTheme.Current;
            Action frame = () => refresh(arg, 5000d, theme);
            Assert.That(Text(tile, "Seconds"), Is.EqualTo("9"), "set-up: the recharging Wish is drawn");
            for (int i = 0; i < 5; i++) frame();   // warm up
            AllocationMeter.AssertNone(frame, 200, "an unchanged Wish tile refresh");
        }
    }
}
