using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
    /// P-UI-HUD-GARBAGE-1, "nothing stale": the HUD skips a write only when what it shows has not changed, and shows the change at once
    /// when it has. A marker written into a text by hand proves a skip (Set compares with the live text, so a rewrite replaces it);
    /// an exact expected string proves a rewrite. Direct calls on a whole MatchHud (HudGarbageRig); no frame timing.
    /// Lines marked "guard" are green on the base, which rewrites on every change; they stay green.
    /// </summary>
    public sealed class HudStaleEditModeTests
    {
        const string Marker = "MARKER";
        HudGarbageRig _rig;
        UiTheme _theme;
        float? _savedScale;

        MatchHud Hud => _rig.Hud;

        [TearDown]
        public void TearDown()
        {
            HudIconHint.CloseAll();
            if (_savedScale.HasValue && _rig != null)
            {
                var settings = HudGarbageRig.GetF(Hud, "_showcaseSettings");
                settings.GetType().GetField("_textScale", HudGarbageRig.All).SetValue(settings, _savedScale.Value);
            }
            _savedScale = null;
            if (_rig != null) _rig.Dispose();   // restores SecondsClock, UiTheme.Current and EventSystem.current
            _rig = null;
        }

        static TMP_Text Label(object owner, string field) => (TMP_Text)HudGarbageRig.GetF(owner, field);
        static TMP_Text At(object owner, string field, int i) => ((TMP_Text[])HudGarbageRig.GetF(owner, field))[i];
        static void Mark(TMP_Text t) { t.text = Marker; }
        static string Hex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        // ------------------------------------------------------------------ 7: champion rows and tooltip (cut 1)

        const string DamageUnavailable = "Combat overview\nRaw values are before target mitigation; this is not measured DPS.";

        static WireBuildSummary WithCode(WireBuildSummary s, byte code, int value)
        {
            var copy = new WireBuildSummary { Gains = (int[])s.Gains.Clone(), MobRows = (WireMobBonusRow[])s.MobRows.Clone() };
            var panel = s.Panel.ToList();
            int at = panel.FindIndex(p => p.Code == code);
            if (at >= 0) panel[at] = new WirePanelValue { Code = code, Value = value };
            else
            {
                int into = panel.FindIndex(p => p.Code > code);
                panel.Insert(into < 0 ? panel.Count : into, new WirePanelValue { Code = code, Value = value });
            }
            copy.Panel = panel.ToArray();
            return copy;
        }

        static string Tooltip(MatchHud hud, WireBuildSummary s, SelfView me)
        {
            double? asMilli = me.OwnerCombat.HasValue ? me.OwnerCombat.Value.AsMilli : (double?)null;
            return DamageUnavailable + "\n" + HudContentText.AutoTooltip(s, hud.HudContent, me.Faction, me.Weapon, asMilli);
        }

        [Test]
        public void TheRowsAreRightAndTheTooltipIsRebuiltOnlyWhenNeeded()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var apply = HudGarbageRig.Make<Action<WireBuildSummary, SelfView>>(hud, "ApplyChampionRows");
            var variants = Enumerable.Range(0, 64).Select(_rig.Variant).ToArray();
            var me = _rig.Session.Me;
            var hint = (HudIconHint)HudGarbageRig.GetF(hud, "_damageHint");

            // Guard: the three row texts equal ChampionPanelText.Rows across the variants.
            for (int k = 0; k < 64; k++)
            {
                apply(variants[k], me);
                var rows = ChampionPanelText.Rows(variants[k], me.Faction, me.Weapon);
                string what = " (variant " + k + ")";
                Assert.That(At(hud, "_championLabel", 0).text, Is.EqualTo(rows.Auto.Name), "auto label" + what);
                Assert.That(At(hud, "_championLabel", 1).text, Is.EqualTo(rows.Passive.Name), "passive label" + what);
                Assert.That(At(hud, "_championValue", 0).text, Is.EqualTo(rows.Auto.Value), "auto value" + what);
                Assert.That(At(hud, "_championValue", 1).text, Is.EqualTo(rows.Passive.Value), "passive value" + what);
                Assert.That(At(hud, "_championScaling", 0).text, Is.EqualTo(hud.HudContent != null ? HudContentText.AutoScaling(hud.HudContent, me.Weapon) : rows.Auto.Scaling), "auto scaling" + what);
                Assert.That(At(hud, "_championScaling", 1).text, Is.EqualTo(rows.Passive.Scaling), "passive scaling" + what);
            }

            // Closed tooltip, only the roam codes move: the tooltip is not rebuilt (red on base).
            apply(variants[10], me);
            Assert.That(hint.IsVisible, Is.False, "set-up: the tooltip is closed");
            hint.Text = Marker;
            apply(variants[11], me);
            Assert.That(hint.Text, Is.EqualTo(Marker), "a closed tooltip is left alone while only the roam codes move");

            // It opens (no refresh runs), then the next call repairs it (guard), and an open tooltip follows the ramp (guard).
            Assert.That(hint.isActiveAndEnabled, Is.True, "set-up: the hint is active (Active match, owner alive)");
            hint.OnPointerEnter(null);
            Assert.That(hint.IsVisible, Is.True, "the pointer opens the hint");
            apply(variants[11], me);
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, variants[11], me)), "guard: a stale tooltip is repaired by the next call after it opens");
            apply(variants[12], me);
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, variants[12], me)), "guard: an open tooltip follows the roam charge");

            // Closed again: left alone (red on base).
            hint.OnPointerExit(null);
            Assert.That(hint.IsVisible, Is.False, "set-up: the hint closed");
            hint.Text = Marker;
            apply(variants[13], me);
            Assert.That(hint.Text, Is.EqualTo(Marker), "a closed tooltip is left alone after the pointer left");
            apply(variants[13], me);   // settle: the same variant again changes nothing

            // Guards: each input of the rows' key rewrites what it feeds.
            var baseSummary = variants[13];
            Mark(At(hud, "_championValue", 1));
            var health = WithCode(baseSummary, 4, 50000);
            apply(health, me);
            Assert.That(At(hud, "_championValue", 1).text, Is.EqualTo(ChampionPanelText.Rows(health, me.Faction, me.Weapon).Passive.Value), "guard: code 4 rewrites the passive value");

            Mark(At(hud, "_championValue", 0)); hint.Text = Marker;
            var offense = WithCode(health, 0, 123000);
            apply(offense, me);
            Assert.That(At(hud, "_championValue", 0).text, Is.EqualTo(ChampionPanelText.Rows(offense, me.Faction, me.Weapon).Auto.Value), "guard: code 0 rewrites the auto value");
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, offense, me)), "guard: code 0 rewrites the tooltip");

            var weapon = me.Weapon;
            Mark(At(hud, "_championLabel", 0)); Mark(At(hud, "_championScaling", 0));
            me.Weapon = WeaponId.Bow;
            apply(offense, me);
            Assert.That(At(hud, "_championLabel", 0).text, Is.EqualTo(ChampionPanelText.Rows(offense, me.Faction, WeaponId.Bow).Auto.Name), "guard: a new weapon rewrites the auto label");
            Assert.That(At(hud, "_championScaling", 0).text, Is.EqualTo(HudContentText.AutoScaling(hud.HudContent, WeaponId.Bow)), "guard: a new weapon rewrites the scaling line");
            me.Weapon = weapon;
            apply(offense, me);

            var originalBlock = hud.HudContent;
            Mark(At(hud, "_championScaling", 0)); hint.Text = Marker;
            hud.HudContent = HudGarbageRig.FixtureHud();
            apply(offense, me);
            Assert.That(At(hud, "_championScaling", 0).text, Is.EqualTo(HudContentText.AutoScaling(hud.HudContent, me.Weapon)), "guard: a new HUD block rewrites the scaling line");
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, offense, me)), "guard: a new HUD block rewrites the tooltip");
            hud.HudContent = originalBlock;
            apply(offense, me);

            var faction = me.Faction;
            Mark(At(hud, "_championLabel", 1)); Mark(At(hud, "_championValue", 0)); Mark(At(hud, "_championScaling", 1));
            me.Faction = FactionId.Holy;
            apply(offense, me);
            var holy = ChampionPanelText.Rows(offense, FactionId.Holy, me.Weapon);
            Assert.That(At(hud, "_championLabel", 1).text, Is.EqualTo(holy.Passive.Name), "guard: a new faction rewrites the passive label");
            Assert.That(At(hud, "_championValue", 0).text, Is.EqualTo(holy.Auto.Value), "guard: a new faction rewrites the auto value");
            Assert.That(At(hud, "_championScaling", 1).text, Is.EqualTo(holy.Passive.Scaling), "guard: a new faction rewrites the passive scaling");
            me.Faction = faction;
            apply(offense, me);

            var combat = Activator.CreateInstance(typeof(OwnerCombatView), true);
            typeof(OwnerCombatView).GetField("<AsMilli>k__BackingField", HudGarbageRig.All).SetValue(combat, 1500d);
            var priorCombat = me.OwnerCombat;
            hint.Text = Marker; Mark(At(hud, "_championValue", 0));
            me.OwnerCombat = (OwnerCombatView)combat;
            apply(offense, me);
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, offense, me)), "guard: a new attack-speed mirror rewrites the tooltip");
            Assert.That(At(hud, "_championValue", 0).text, Is.EqualTo(ChampionPanelText.Rows(offense, me.Faction, me.Weapon).Auto.Value), "guard: a new attack-speed mirror rewrites the rows");
            me.OwnerCombat = priorCombat;
            apply(offense, me);

            Mark(At(hud, "_championValue", 0)); hint.Text = Marker;
            HudGarbageRig.SetF(hud, "_panelShownCount", -1);
            apply(offense, me);
            Assert.That(At(hud, "_championValue", 0).text, Is.EqualTo(ChampionPanelText.Rows(offense, me.Faction, me.Weapon).Auto.Value), "guard: -1 rebuilds the rows now");
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, offense, me)), "guard: -1 rebuilds the tooltip now");

            // After a text-scale change the open tooltip is current at the next call (guard).
            var settings = HudGarbageRig.GetF(hud, "_showcaseSettings");
            var scaleField = settings.GetType().GetField("_textScale", HudGarbageRig.All);
            _savedScale = (float)scaleField.GetValue(settings);
            hint.OnPointerEnter(null);
            Assert.That(hint.IsVisible, Is.True, "set-up: the hint is open for the scale change");
            scaleField.SetValue(settings, 1.25f);
            HudGarbageRig.Call(hud, "ApplyTextScale");
            var next = WithCode(offense, 8, 999000);
            apply(next, me);
            Assert.That(hint.Text, Is.EqualTo(Tooltip(hud, next, me)), "guard: after a text-scale change the open tooltip is current at the next call");
            hint.OnPointerExit(null);
        }

        // ------------------------------------------------------------------ 8: the hp label (cut 2)

        [Test]
        public void TheHpLabelFollowsItsInputs()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var refresh = HudGarbageRig.Make<Action<SelfView, double>>(hud, "RefreshLegacyHud");
            var me = _rig.Session.Me;
            double now = _rig.Session.ServerTimeMs;
            var label = Label(hud, "_hpValue");
            refresh(me, now);

            // The one red line: the same inputs, no Clear in between, and a marker written by hand survives.
            Mark(label);
            refresh(me, now);
            Assert.That(label.text, Is.EqualTo(Marker), "the hp label is not rewritten while hp, max, barrier, bounty and the accent stay");

            // Guards: the exact text after each input changes (a marker first, so a skipped write shows).
            Func<string> accentTag = () => Hex(UiTheme.Current.Accent);
            Func<float, float, float, int, string> text = (hp, max, barrier, bounty) =>
                hp.ToString("0") + "/" + max.ToString("0") + (barrier > 0 ? " +" + barrier.ToString("0") : "") +
                (bounty > 0 ? " <color=" + accentTag() + "> · Bounty " + bounty + "</color>" : "");
            me.Hp = 1234; me.MaxHp = 1234; me.Barrier = 0; me.Bounty = 0;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo(text(1234, 1234, 0, 0)), "guard: hp and max change");
            me.Hp = 1200;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo(text(1200, 1234, 0, 0)), "guard: hp alone changes");
            me.MaxHp = 1300;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo(text(1200, 1300, 0, 0)), "guard: max alone changes");
            me.Barrier = 123;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo(text(1200, 1300, 123, 0)), "guard: barrier changes");
            me.Hp = 1234; me.MaxHp = 1234; me.Bounty = 12;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo(text(1234, 1234, 123, 12)), "guard: bounty changes (the default accent colours the tag)");
            Assert.That(label.text, Is.EqualTo("1234/1234 +123 <color=" + Hex(UiTheme.Skin().Accent) + "> · Bounty 12</color>"), "guard: the tag colour is the theme's Accent");

            // Guard: another Accent, the colour tag follows.
            _theme = UiTheme.Skin();
            _theme.Accent = new Color32(10, 200, 30, 255);
            UiTheme.Current = _theme;
            Mark(label); refresh(me, now);
            Assert.That(label.text, Is.EqualTo("1234/1234 +123 <color=#0AC81E> · Bounty 12</color>"), "guard: a new Accent recolours the bounty tag");

            // Guard: Clear and Bind reset the key, so the same inputs write again.
            Mark(label);
            HudGarbageRig.Call(hud, "ClearOwnerPresentation", "Awaiting server");
            refresh(me, now);
            Assert.That(label.text, Is.EqualTo("1234/1234 +123 <color=#0AC81E> · Bounty 12</color>"), "guard: the label is written again after ClearOwnerPresentation");
            Mark(label);
            hud.Bind(_rig.Session);
            refresh(me, now);
            Assert.That(label.text, Is.EqualTo("1234/1234 +123 <color=#0AC81E> · Bounty 12</color>"), "guard: the label is written again after Bind");
        }

        // ------------------------------------------------------------------ 9: the vitals strings (cut 3)

        static string VitalsText(SelfView me) => ShowcasePalette.GetFactionName((int)me.Faction) + " · HP " + me.Hp.ToString("0.#") + " / " + me.MaxHp.ToString("0.#") + " · Barrier " + me.Barrier.ToString("0.#");
        static string StatsText(SelfView me) => "Kills " + me.Kills + " · GV " + me.Gv + " · " + PlayerWords.Word(me.Status);

        [Test]
        public void TheVitalsStringsAreCurrentWhenTheirLabelIsShown()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var refresh = HudGarbageRig.Make<Action<SelfView, double>>(hud, "RefreshOwnerPresentation");
            var setPlate = HudGarbageRig.Make<Action<bool>>(hud, "SetPlateMode");
            var me = _rig.Session.Me;
            double now = _rig.Session.ServerTimeMs;
            var vitals = Label(hud, "_vitals"); var stats = Label(hud, "_combatStats");
            Assert.That((bool)HudGarbageRig.GetF(hud, "_plateActive"), Is.True, "set-up: the plate is active");
            Assert.That(vitals.gameObject.activeSelf || stats.gameObject.activeSelf, Is.False, "set-up: both labels are hidden while the plate is active");
            refresh(me, now);
            string vitalsBefore = vitals.text, statsBefore = stats.text;

            // Red on base: a hidden label keeps the text it read before while kills, then GV, move.
            me.Kills = 2; refresh(me, now);
            Assert.That(vitals.text, Is.EqualTo(vitalsBefore), "the hidden vitals label is not rebuilt (kills 2)");
            Assert.That(stats.text, Is.EqualTo(statsBefore), "the hidden kills label is not rebuilt (kills 2)");
            me.Kills = 5; refresh(me, now);
            Assert.That(stats.text, Is.EqualTo(statsBefore), "the hidden kills label is not rebuilt (kills 5)");
            me.Gv = 3; refresh(me, now);
            Assert.That(vitals.text, Is.EqualTo(vitalsBefore), "the hidden vitals label is not rebuilt (GV 3)");
            Assert.That(stats.text, Is.EqualTo(statsBefore), "the hidden kills label is not rebuilt (GV 3)");
            me.Hp = 800; refresh(me, now);
            Assert.That(vitals.text, Is.EqualTo(vitalsBefore), "the hidden vitals label is not rebuilt (hp 800)");
            Assert.That(stats.text, Is.EqualTo(statsBefore), "the hidden kills label is not rebuilt (hp 800)");

            // Guard: showing the labels makes them current with no frame.
            setPlate(false);
            Assert.That(vitals.gameObject.activeSelf && stats.gameObject.activeSelf, Is.True, "set-up: SetPlateMode(false) shows both labels");
            Assert.That(vitals.text, Is.EqualTo(VitalsText(me)), "guard: SetPlateMode(false) alone makes the vitals current");
            Assert.That(stats.text, Is.EqualTo(StatsText(me)), "guard: SetPlateMode(false) alone makes the kills line current");

            // Guard: a snapshot that is Dead flips the plate inside the refresh; both labels are current in that call.
            _rig.Snap(); _rig.Frame();
            Assert.That((bool)HudGarbageRig.GetF(hud, "_plateActive"), Is.True, "set-up: an alive snapshot puts the plate back");
            _rig.Snap(s => { s.Kills = 7; s.Gv = 9; s.Hp = 0; s.Status = (byte)ActorStatus.Dead; });
            _rig.Frame();
            me = _rig.Session.Me;
            Assert.That(me.Status, Is.EqualTo(ActorStatus.Dead), "set-up: the dead snapshot arrived");
            Assert.That((bool)HudGarbageRig.GetF(hud, "_plateActive"), Is.False, "set-up: the plate left the active mode");
            Assert.That(vitals.text, Is.EqualTo(VitalsText(me)), "guard: the vitals are current in the call that flips the plate (Dead)");
            Assert.That(stats.text, Is.EqualTo(StatsText(me)), "guard: the kills line is current in the call that flips the plate (Dead)");

            // Guard: Clear writes the waiting texts, and showing the labels keeps them, also when a flag was stale while the plate was active.
            _rig.Snap(); _rig.Frame();
            me = _rig.Session.Me;
            Assert.That((bool)HudGarbageRig.GetF(hud, "_plateActive"), Is.True, "set-up: alive again, the plate is active");
            me.Kills = (ushort)(me.Kills + 4); refresh(me, _rig.Session.ServerTimeMs);   // the labels are hidden: stale
            HudGarbageRig.Call(hud, "ClearOwnerPresentation", "Awaiting server");
            Assert.That(vitals.text, Is.EqualTo("Waiting for player state"), "guard: Clear writes the waiting vitals text");
            Assert.That(stats.text, Is.EqualTo("Awaiting player stats"), "guard: Clear writes the waiting kills text");
            setPlate(false);
            Assert.That(vitals.text, Is.EqualTo("Waiting for player state"), "guard: SetPlateMode(false) keeps the waiting vitals text");
            Assert.That(stats.text, Is.EqualTo("Awaiting player stats"), "guard: SetPlateMode(false) keeps the waiting kills text");
        }

        // ------------------------------------------------------------------ 10: the drawer (cut 4)

        [Test]
        public void TheDrawerOpensWithCurrentNumbers()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var drawer = (BuildDrawer)HudGarbageRig.GetF(hud, "_drawer");
            var vitals = Label(drawer, "_vitals");
            Assert.That(drawer.IsVisible, Is.False, "set-up: the drawer is closed");

            HudGarbageRig.Call(hud, "ToggleBuild");
            Assert.That(drawer.IsVisible, Is.True, "set-up: ToggleBuild opens it");
            _rig.Frame();
            string opened = "HP 950 / 950     Barrier 0";
            Assert.That(vitals.text, Is.EqualTo(opened), "guard: the first open shows the owner's hp");
            HudGarbageRig.Call(hud, "ToggleBuild");
            Assert.That(drawer.IsVisible, Is.False, "set-up: ToggleBuild closes it");

            var cards = new byte[80]; cards[0] = 2; cards[5] = 1;
            var boosts = new byte[8]; boosts[1] = 3;
            _rig.Snap(s => s.Hp = 700);
            _rig.State(new BuildState { Value = new WireBuild { Cards = cards, Boosts = boosts } });
            _rig.Frame(); _rig.Frame();
            Assert.That(_rig.Session.Me.Hp, Is.EqualTo(700f), "set-up: the new hp arrived");
            Assert.That(_rig.Session.Collections.HasBuild, Is.True, "set-up: the build arrived");
            Assert.That(vitals.text, Is.EqualTo(opened), "a closed drawer is not refreshed by the frames");

            HudGarbageRig.Call(hud, "ToggleBuild");
            Assert.That(drawer.IsVisible, Is.True, "set-up: it opens again, and no frame has run");
            Assert.That(vitals.text, Is.EqualTo("HP 700 / 950     Barrier 0"), "guard: it opens with the current hp");
            var counts = (TMP_Text[])HudGarbageRig.GetF(drawer, "_cardCounts");
            Assert.That(counts[0].text, Is.EqualTo("×2"), "guard: it opens with the current card counts (card 0)");
            Assert.That(counts[5].text, Is.EqualTo("×1"), "guard: it opens with the current card counts (card 5)");
            Assert.That(counts[1].text, Is.EqualTo("×0"), "guard: it opens with the current card counts (card 1)");
        }

        // ------------------------------------------------------------------ 11: the weapon name (cut 5), a standing guard

        [Test]
        public void TheWeaponNameIsTheEnumNameForEveryValue()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var readout = HudGarbageRig.Make<Action<SelfView>>(hud, "RefreshDamageReadout");
            var label = Label(hud, "_damageWeapon");
            var sword = new SelfView { Id = 1, Hp = 100, MaxHp = 100, Status = ActorStatus.Alive, Weapon = WeaponId.Sword };
            readout(sword);
            Assert.That(label.text, Is.EqualTo("Sword"), "guard: the first weapon read is Sword");
            readout(null);
            Assert.That(label.text, Is.EqualTo("—"), "guard: no owner reads the dash");
            readout(sword);
            Assert.That(label.text, Is.EqualTo("Sword"), "guard: Sword again");
            sword.Weapon = WeaponId.Fists; readout(sword);
            Assert.That(label.text, Is.EqualTo("Fists"), "guard: Sword to Fists");
            foreach (WeaponId id in Enum.GetValues(typeof(WeaponId)).Cast<WeaponId>().Concat(new[] { (WeaponId)99, (WeaponId)6, (WeaponId)255 }))
            {
                sword.Weapon = id; readout(sword);
                Assert.That(label.text, Is.EqualTo(id.ToString()), "guard: the weapon " + (int)id + " reads exactly ToString()");
            }

            // The slot names, through RefreshBuildIcons alone (RefreshCollections would let the keyed hints write the same names).
            var icons = HudGarbageRig.Make<Action<CollectionsView>>(hud, "RefreshBuildIcons");
            var names = (TMP_Text[])HudGarbageRig.GetF(hud, "_slotNames");
            _rig.State(new InventoryState { Value = new WireInventory { ActiveSlot = 0, Slot0 = (byte)WeaponId.Axe, Slot1 = (byte)WeaponId.Bow } });
            Mark(names[0]); Mark(names[1]);
            icons(_rig.Session.Collections);
            Assert.That(names[0].text, Is.EqualTo("Axe"), "guard: slot 0 reads Axe");
            Assert.That(names[1].text, Is.EqualTo("Bow"), "guard: slot 1 reads Bow");
            _rig.State(new InventoryState { Value = new WireInventory { ActiveSlot = 1, Slot0 = (byte)WeaponId.Bow, Slot1 = (byte)WeaponId.Axe } });
            Mark(names[0]); Mark(names[1]);
            icons(_rig.Session.Collections);
            Assert.That(names[0].text, Is.EqualTo("Bow"), "guard: swapped, slot 0 reads Bow");
            Assert.That(names[1].text, Is.EqualTo("Axe"), "guard: swapped, slot 1 reads Axe");
            _rig.State(new InventoryState { Value = new WireInventory { ActiveSlot = 0, Slot0 = (byte)WeaponId.Bow, Slot1 = null } });
            Mark(names[0]); Mark(names[1]);
            icons(_rig.Session.Collections);
            Assert.That(names[0].text, Is.EqualTo("Bow"), "guard: slot 0 still reads Bow");
            Assert.That(names[1].text, Is.EqualTo("Empty"), "guard: the empty slot reads Empty");
        }

        // ------------------------------------------------------------------ 12: the build summary (cut 6)

        static readonly int[] BaseGains = { 240000, 0, 1900, 1250, 325, 1500000, 0, 15000, 500 };

        static WireBuildSummary Summary(int[] gains, params WireMobBonusRow[] mobs)
        {
            var panel = HudGarbageRig.FixtureSummary().Panel;
            return new WireBuildSummary { Gains = (int[])gains.Clone(), MobRows = (WireMobBonusRow[])mobs.Clone(), Panel = (WirePanelValue[])panel.Clone() };
        }

        static WireMobBonusRow Mob(byte type, byte stat, uint stacks, int total) => new WireMobBonusRow { MobType = type, Stat = stat, Stacks = stacks, Total = total };

        static string MobLine(WireBuildSummary s)
        {
            var lines = BuildHudText.MobLines(s); var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++) sb.Append(i > 0 ? " · " : "").Append(lines[i].Label).Append(lines[i].Total.Length > 0 ? " " + lines[i].Total : "");
            return sb.ToString();
        }

        void Deliver(WireBuildSummary s) { _rig.State(new BuildSummaryState { Value = s }); }

        /// <summary>The nine cells, the mob line, the BUILD block and the header with its hint, each a marker.</summary>
        void MarkAll()
        {
            for (int i = 0; i < 9; i++) Mark(At(Hud, "_gainValues", i));
            Mark(Label(Hud, "_mobText")); Mark(Label(Hud, "_build")); Mark(Label(Hud, "_cardsText")); Mark(Label(Hud, "_gainsHeader"));
            ((HudIconHint)HudGarbageRig.GetF(Hud, "_buildHint")).Text = Marker;
            ((HudIconHint)HudGarbageRig.GetF(Hud, "_gainsHint")).Text = Marker;
        }

        void AssertMarkers(string what, params int[] exceptCells)
        {
            for (int i = 0; i < 9; i++)
                if (!exceptCells.Contains(i)) Assert.That(At(Hud, "_gainValues", i).text, Is.EqualTo(Marker), what + ": cell " + i + " is not rewritten");
        }

        [Test]
        public void EachBuildPartFollowsItsInputsWhenANewSummaryArrives()
        {
            _rig = new HudGarbageRig();
            var hud = Hud;
            var cards = new byte[80]; cards[0] = 2; cards[5] = 1;
            var boosts = new byte[8]; boosts[1] = 3;
            _rig.State(new BuildState { Value = new WireBuild { Cards = cards, Boosts = boosts } });
            var mobs = new[] { Mob(0, 2, 3, 500), Mob(1, 3, 2, 300) };
            var first = Summary(BaseGains, mobs);
            Deliver(first);
            Assert.That(hud.GainedCellText(8), Is.EqualTo(BuildHudText.Cells(first)[8].Value), "set-up: the first summary drew its cells");
            Assert.That(Label(hud, "_mobText").text, Is.EqualTo(MobLine(first)), "set-up: the first summary drew the mob line");
            var buildHint = (HudIconHint)HudGarbageRig.GetF(hud, "_buildHint");
            var gainsHint = (HudIconHint)HudGarbageRig.GetF(hud, "_gainsHint");
            var header = Label(hud, "_gainsHeader");
            var cardsText = Label(hud, "_cardsText");
            var build = Label(hud, "_build");
            var mobText = Label(hud, "_mobText");
            Assert.That(build.text, Is.EqualTo(BuildHudText.ExtrasHeader(2, first)), "set-up: the BUILD line counts the cards and mob types");

            // (a) A new object of equal content in fresh arrays: every marker survives (red on base).
            MarkAll();
            Deliver(Summary(BaseGains, mobs));
            AssertMarkers("(a)");
            Assert.That(mobText.text, Is.EqualTo(Marker), "(a) the mob line is not rewritten");
            Assert.That(build.text, Is.EqualTo(Marker), "(a) the BUILD line is not rewritten");
            Assert.That(buildHint.Text, Is.EqualTo(Marker), "(a) the B hint is not rewritten");
            Assert.That(cardsText.text, Is.EqualTo(Marker), "(a) the cards row is not rewritten");
            Assert.That(header.text, Is.EqualTo(Marker), "(a) the gains header is not rewritten");
            Assert.That(gainsHint.Text, Is.EqualTo(Marker), "(a) the gains hint is not rewritten");

            // (b) Only Gains[8] differs: that cell follows, every other marker survives (red on base).
            var gains = (int[])BaseGains.Clone(); gains[8] = 650;
            var second = Summary(gains, mobs);
            MarkAll();
            Deliver(second);
            Assert.That(hud.GainedCellText(8), Is.EqualTo(BuildHudText.Cells(second)[8].Value), "(b) cell 8 follows its raw gain");
            AssertMarkers("(b)", 8);
            Assert.That(mobText.text, Is.EqualTo(Marker), "(b) the mob line is not rewritten");
            Assert.That(build.text, Is.EqualTo(Marker), "(b) the BUILD line is not rewritten");
            Assert.That(buildHint.Text, Is.EqualTo(Marker), "(b) the B hint is not rewritten");
            Assert.That(cardsText.text, Is.EqualTo(Marker), "(b) the cards row is not rewritten");
            Assert.That(header.text, Is.EqualTo(Marker), "(b) the open gains header is not rewritten");
            Assert.That(gainsHint.Text, Is.EqualTo(Marker), "(b) the open gains hint is not rewritten");

            // (c) A mob row's Stacks differs: the mob line follows, the rest survives; then a new mob type: the BUILD block runs.
            var stacked = Summary(gains, Mob(0, 2, 4, 667), mobs[1]);
            MarkAll();
            Deliver(stacked);
            Assert.That(mobText.text, Is.EqualTo(MobLine(stacked)), "(c) the mob line follows a Stacks change");
            AssertMarkers("(c)");
            Assert.That(build.text, Is.EqualTo(Marker), "(c) the BUILD line is not rewritten by a Stacks change");
            Assert.That(buildHint.Text, Is.EqualTo(Marker), "(c) the B hint is not rewritten by a Stacks change");
            Assert.That(cardsText.text, Is.EqualTo(Marker), "(c) the cards row is not rewritten by a Stacks change");
            var typed = Summary(gains, Mob(0, 2, 4, 667), mobs[1], Mob(2, 4, 1, 900));
            MarkAll();
            Deliver(typed);
            Assert.That(mobText.text, Is.EqualTo(MobLine(typed)), "(c) the mob line follows a new mob type");
            Assert.That(build.text, Is.EqualTo(BuildHudText.ExtrasHeader(2, typed)), "(c) the BUILD line counts the new mob type");
            Assert.That(buildHint.Text, Is.Not.EqualTo(Marker), "(c) the B hint is rewritten (the block ran)");
            Assert.That(cardsText.text, Is.Not.EqualTo(Marker), "(c) the cards row is rewritten (the block ran)");
            AssertMarkers("(c) new type");

            // (d) Guards: a cell that goes from zero to non-zero follows in colour and weight; cards or boosts change the BUILD block.
            var theme = UiTheme.Current;
            var zero = At(hud, "_gainValues", 6);
            Assert.That(zero.fontStyle, Is.EqualTo(FontStyles.Normal), "set-up: a zero cell is regular");
            Assert.That((Color32)zero.color, Is.EqualTo(theme.TextSecondary), "set-up: a zero cell is muted");
            var risen = (int[])gains.Clone(); risen[6] = 12000;
            var third = Summary(risen, typed.MobRows);
            Deliver(third);
            Assert.That(zero.text, Is.EqualTo(BuildHudText.Cells(third)[6].Value), "guard: cell 6 follows its raw gain");
            Assert.That(zero.fontStyle, Is.EqualTo(FontStyles.Bold), "guard: a non-zero cell is bold");
            Assert.That((Color32)zero.color, Is.EqualTo(theme.TextPrimary), "guard: a non-zero cell is primary ink");
            var cards2 = (byte[])cards.Clone(); cards2[9] = 1;
            Mark(build); Mark(cardsText);
            _rig.State(new BuildState { Value = new WireBuild { Cards = cards2, Boosts = boosts } });
            Assert.That(build.text, Is.EqualTo(BuildHudText.ExtrasHeader(3, third)), "guard: a new card type rewrites the BUILD line");
            Assert.That(cardsText.text, Does.StartWith("Cards: ").And.Not.EqualTo(Marker), "guard: a new card type rewrites the cards row");
            var boosts2 = (byte[])boosts.Clone(); boosts2[2] = 1;
            Mark(build); Mark(cardsText);
            _rig.State(new BuildState { Value = new WireBuild { Cards = cards2, Boosts = boosts2 } });
            Assert.That(build.text, Is.EqualTo(BuildHudText.ExtrasHeader(3, third)), "guard: a boost change rewrites the BUILD line");
            Assert.That(cardsText.text, Is.Not.EqualTo(Marker), "guard: a boost change rewrites the cards row");

            // (e) The header: folded, a changed cell rewrites header and hint (guard); open, its markers survive any cell change (red on base).
            HudGarbageRig.Call(hud, "ApplyFolds", true, false);
            var fourthGains = (int[])risen.Clone(); fourthGains[0] = 250000;
            var fourth = Summary(fourthGains, typed.MobRows);
            header.text = Marker; gainsHint.Text = Marker;
            Deliver(fourth);
            Assert.That(header.text, Is.EqualTo(LeftEdgeText.GainedHeader(BuildHudText.Cells(fourth), true)), "guard: a folded header follows a cell change");
            Assert.That(gainsHint.Text, Is.EqualTo(LeftEdgeText.GainedHint(BuildHudText.Cells(fourth), true)), "guard: a folded hint follows a cell change");
            HudGarbageRig.Call(hud, "ApplyFolds", false, false);
            header.text = Marker; gainsHint.Text = Marker;
            var fifthGains = (int[])fourthGains.Clone(); fifthGains[3] = 1300;
            Deliver(Summary(fifthGains, typed.MobRows));
            Assert.That(header.text, Is.EqualTo(Marker), "(e) an open header's text survives a cell change");
            Assert.That(gainsHint.Text, Is.EqualTo(Marker), "(e) an open header's hint survives a cell change");
            var sixthGains = (int[])fifthGains.Clone(); sixthGains[1] = 700;   // cell 1 was zero
            var sixth = Summary(sixthGains, typed.MobRows);
            header.text = Marker; gainsHint.Text = Marker;
            Deliver(sixth);
            Assert.That(header.text, Is.EqualTo(Marker), "(e) an open header's text survives a zero cell turning non-zero");
            HudGarbageRig.Call(hud, "ApplyFolds", true, false);
            Assert.That(header.text, Is.EqualTo(LeftEdgeText.GainedHeader(BuildHudText.Cells(sixth), true)), "guard: folding shows the current summary's count");
            Assert.That(gainsHint.Text, Is.EqualTo(LeftEdgeText.GainedHint(BuildHudText.Cells(sixth), true)), "guard: folding shows the current summary's hint");
            HudGarbageRig.Call(hud, "ApplyFolds", false, false);

            // (f) A null summary hides the strip and the mob row; a new one shows them with the right texts; folded, S, none, S reads S.
            _rig.State(new BuildSummaryState { Value = null });
            Assert.That(_rig.Session.Collections.Summary, Is.Null, "set-up: the summary was reset");
            Assert.That(hud.GainedStripVisible, Is.False, "(f) the GAINED strip is hidden with no summary");
            Assert.That(mobText.gameObject.activeSelf, Is.False, "(f) the mob row is hidden with no summary");
            var again = Summary(sixthGains, typed.MobRows);
            Deliver(again);
            Assert.That(hud.GainedStripVisible, Is.True, "(f) the strip shows again");
            Assert.That(mobText.gameObject.activeSelf, Is.True, "(f) the mob row shows again");
            var cellsAgain = BuildHudText.Cells(again);
            for (int i = 0; i < 9; i++) Assert.That(hud.GainedCellText(i), Is.EqualTo(cellsAgain[i].Value), "(f) cell " + i + " after the reset");
            Assert.That(mobText.text, Is.EqualTo(MobLine(again)), "(f) the mob line after the reset");
            HudGarbageRig.Call(hud, "ApplyFolds", true, false);
            Deliver(Summary(sixthGains, typed.MobRows));
            _rig.State(new BuildSummaryState { Value = null });
            var last = Summary(sixthGains, typed.MobRows);
            Deliver(last);
            Assert.That(header.text, Is.EqualTo(LeftEdgeText.GainedHeader(BuildHudText.Cells(last), true)), "(f) folded, S, none, S: the header's count is S's");
            Assert.That(gainsHint.Text, Is.EqualTo(LeftEdgeText.GainedHint(BuildHudText.Cells(last), true)), "(f) folded, S, none, S: the hint is S's");
            HudGarbageRig.Call(hud, "ApplyFolds", false, false);

            // (g) After a theme switch an equal summary in a fresh object recolours the muted and the primary cells (guard).
            // First settle on a summary whose muted cells already read 0, so the next one changes no raw value: only the key can force the rewrite.
            var settled = Summary(BaseGains, typed.MobRows);
            Deliver(settled);
            var cellsSettled = BuildHudText.Cells(settled);
            Assert.That(cellsSettled.Any(c => c.Dim) && cellsSettled.Any(c => !c.Dim), Is.True, "set-up: the settled summary has muted and primary cells");
            _theme = UiTheme.Skin();
            _theme.TextPrimary = new Color32(11, 22, 33, 255);
            _theme.TextSecondary = new Color32(44, 55, 66, 255);
            UiTheme.Current = _theme;
            var themed = Summary(BaseGains, typed.MobRows);
            Deliver(themed);
            var cellsTheme = BuildHudText.Cells(themed);
            Assert.That(cellsTheme.Any(c => c.Dim) && cellsTheme.Any(c => !c.Dim), Is.True, "set-up: the summary has muted and primary cells");
            for (int i = 0; i < 9; i++)
                Assert.That((Color32)At(hud, "_gainValues", i).color, Is.EqualTo(cellsTheme[i].Dim ? _theme.TextSecondary : _theme.TextPrimary), "guard: cell " + i + " follows the theme");
        }
    }
}
