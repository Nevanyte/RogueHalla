using System;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PLAYER-WORDS-2, owner facts: the Wish reads in the game's words (card, tile name, hint), the plate stats line carries the status
    /// word rather than the code name, and the word tables allocate nothing. The rig is that of MatchHudIconPresentationEditModeTests: a
    /// MatchHud with only its combat HUD built, fed an owner through RefreshOwnerPresentation. Nothing here reads a clock.
    /// </summary>
    public sealed class OwnerWordsEditModeTests
    {
        GameObject _root;
        MatchHud _hud;
        ShowcaseSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Owner words test", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
                typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_settings, 1f);
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
            _root = null; _hud = null; _settings = null;
        }

        Transform WishTile => _root.transform.Find("HudStage/PlayerHUD/CombatActions/WishTile");
        string TileName => WishTile.Find("Name/Label").GetComponent<TMP_Text>().text;

        [Test]
        public void TheWishReadsInTheGamesWords()
        {
            var card = (TMP_Text)GetField("_wishCard");
            var hint = (HudIconHint)GetField("_wishHint");
            Assert.That(card, Is.Not.Null, "set-up: the rig builds the Wish card");
            Assert.That(WishTile, Is.Not.Null, "set-up: the rig builds the Wish tile");

            // Holy: a ready Wish (ReadyAt 4000 is behind the clock at 5000) with a cast landing at 6500 (a cast counts as Ready: the tile shows its name).
            var holy = new SelfView { Hp = 90, MaxHp = 100, Faction = FactionId.Holy, Status = ActorStatus.Alive };
            holy.Wish = new WishView
            {
                Ability = AbilityId.LastStand, Charges = 1, MaxCharges = 1, ReadyAt = 4000, RechargeMs = 4000,
                Cast = new WishCastView { Ability = AbilityId.LastStand, StartedAt = 4500, LandsAt = 6500 }
            };
            Invoke("RefreshOwnerPresentation", holy, 5000d);
            StringAssert.Contains("R · Last Stand · 1/1", card.text);
            StringAssert.Contains("Casting Last Stand · 2", card.text);
            StringAssert.DoesNotContain("LastStand", card.text);
            Assert.That(TileName, Is.EqualTo("Last Stand"), "the tile name");
            StringAssert.Contains("R · Last Stand · 1/1", hint.Text);
            StringAssert.Contains("Casting Last Stand · 2", hint.Text);
            StringAssert.DoesNotContain("LastStand", hint.Text);

            // Fire: Blaze reads Blaze Step on the card, the tile and the hint.
            var fire = new SelfView { Hp = 90, MaxHp = 100, Faction = FactionId.Fire, Status = ActorStatus.Alive };
            fire.Wish = new WishView { Ability = AbilityId.Blaze, Charges = 1, MaxCharges = 2, ReadyAt = 4000, RechargeMs = 4000 };
            Invoke("RefreshOwnerPresentation", fire, 5000d);
            StringAssert.Contains("R · Blaze Step · 1/2", card.text);
            Assert.That(TileName, Is.EqualTo("Blaze Step"), "the tile name");
            StringAssert.Contains("Blaze Step", hint.Text);
        }

        [Test]
        public void TheStatusWordReplacesTheCode()
        {
            // This rig builds no _combatStats (CreateCombatHud does not make it): a label under the root, as MatchHudWishBarrierEditModeTests does.
            var label = new GameObject("Combat stats", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(_root.transform, false);
            SetField("_combatStats", label);

            var me = new SelfView { Hp = 60, MaxHp = 100, Kills = 2, Gv = 4, Status = ActorStatus.Mia };
            Invoke("RefreshOwnerPresentation", me, 5000d);
            Assert.That(label.text, Is.EqualTo("Kills 2 · GV 4 · Missing in action"));
            foreach (var (status, word) in new[] { (ActorStatus.Alive, "Alive"), (ActorStatus.Dead, "Dead"), (ActorStatus.Lobby, "Lobby"), (ActorStatus.Mia, "Missing in action") })
            {
                me.Status = status;
                Invoke("RefreshOwnerPresentation", me, 5000d);
                Assert.That(label.text, Is.EqualTo("Kills 2 · GV 4 · " + word), "status " + status);
            }
        }

        [Test]
        public void TheTablesAllocateNothing()
        {
            Assert.That(PlayerWords.Word(AbilityId.Blaze), Is.EqualTo("Blaze Step"), "set-up: the table has its words");
            var abilities = (AbilityId[])Enum.GetValues(typeof(AbilityId));
            var statuses = (ActorStatus[])Enum.GetValues(typeof(ActorStatus));
            var props = (PropAppearance[])Enum.GetValues(typeof(PropAppearance));
            var boosts = (BoostId[])Enum.GetValues(typeof(BoostId));
            var kinds = (TradeKind[])Enum.GetValues(typeof(TradeKind));
            var stats = (TradeStat[])Enum.GetValues(typeof(TradeStat));
            var states = (SessionState[])Enum.GetValues(typeof(SessionState));
            int sink = 0;
            Action all = () =>
            {
                foreach (var v in abilities) sink += PlayerWords.Word(v).Length;
                foreach (var v in statuses) sink += PlayerWords.Word(v).Length;
                foreach (var v in props) sink += PlayerWords.Word(v).Length;
                foreach (var v in boosts) sink += PlayerWords.Word(v).Length;
                foreach (var v in kinds) sink += PlayerWords.Word(v).Length;
                foreach (var v in stats) sink += PlayerWords.Word(v).Length;
                foreach (var v in states) sink += PlayerWords.Word(v).Length;
            };
            AllocationMeter.AssertNone(all, 200, "PlayerWords.Word over every defined value");
            Assert.That(sink, Is.GreaterThan(0));
        }

        object GetField(string name) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_hud);
        void SetField(string name, object value) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, value);
        void Invoke(string name, params object[] args) => typeof(MatchHud).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_hud, args);
    }
}
