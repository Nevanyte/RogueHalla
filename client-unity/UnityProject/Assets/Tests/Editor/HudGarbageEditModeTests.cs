using System;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-HUD-GARBAGE-1: a server tick that changes nothing the HUD shows allocates nothing in it. Each test measures one call, or one
    /// whole tick, on a whole MatchHud (HudGarbageRig) with AllocationMeter (GC.Alloc samples; GC.GetAllocatedBytesForCurrentThread
    /// reads 0 in this editor). Every message, view and delegate is built before a window; time moves only through the rig's clocks.
    /// The bounded tests keep the whole tick under 1.5 times what the packet's reference implementation measured.
    /// </summary>
    public sealed class HudGarbageEditModeTests
    {
        const int Calls = HudGarbageRig.Calls;

        // GC.Alloc samples a call. M, measured by the oracle author on a throwaway correct implementation of the six cuts: ramp tick
        // 15.75, the State frame alone 12.75, damage tick 6. The limit is ceil(1.5 M) a call: 24, 20 and 9. The base makes 252.5, 61.75
        // and 31 a call, so each limit is at most half of the base (126.25, 30.9 and 15.5).
        const int RampTickLimitPerCall = 24, CollectionsLimitPerCall = 20, DamageTickLimitPerCall = 9;

        HudGarbageRig _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) _rig.Dispose();
            _rig = null;
        }

        static WireBuildSummary Copy(WireBuildSummary s) => new WireBuildSummary
        {
            Gains = (int[])s.Gains.Clone(), MobRows = (WireMobBonusRow[])s.MobRows.Clone(), Panel = (WirePanelValue[])s.Panel.Clone(),
        };

        // ------------------------------------------------------------------ 1 to 5: one call

        [Test]
        public void ChampionRowsAllocateNothingWhileOnlyTheRoamCodesMove()
        {
            _rig = new HudGarbageRig();
            var apply = HudGarbageRig.Make<Action<WireBuildSummary, SelfView>>(_rig.Hud, "ApplyChampionRows");
            var variants = Enumerable.Range(0, 64).Select(_rig.Variant).ToArray();
            var me = _rig.Session.Me;
            apply(variants[0], me);
            int i = 0;
            Action step = () => apply(variants[i++ & 63], me);
            AllocationMeter.AssertNone(step, Calls, "ApplyChampionRows while only the roam charge codes (8, 9, 23, 24) move");
        }

        [Test]
        public void AnOwnerRefreshWithOnlyTheKillsMovingAllocatesNothing()
        {
            _rig = new HudGarbageRig();
            Assert.That((bool)HudGarbageRig.GetF(_rig.Hud, "_plateActive"), Is.True, "set-up: the plate is active");
            var refresh = HudGarbageRig.Make<Action<SelfView, double>>(_rig.Hud, "RefreshOwnerPresentation");
            var me = _rig.Session.Me;
            double now = _rig.Session.ServerTimeMs;
            refresh(me, now);
            Action step = () => { me.Kills++; me.Gv++; refresh(me, now); };
            AllocationMeter.AssertNone(step, Calls, "RefreshOwnerPresentation with the plate active and only Kills and Gv moving");
        }

        [Test]
        public void TheHpLabelAllocatesNothingWhileItsInputsStay()
        {
            _rig = new HudGarbageRig();
            var refresh = HudGarbageRig.Make<Action<SelfView, double>>(_rig.Hud, "RefreshLegacyHud");
            var me = _rig.Session.Me;
            double now = _rig.Session.ServerTimeMs;
            Action step = () => refresh(me, now);
            AllocationMeter.AssertNone(step, Calls, "RefreshLegacyHud with the hp label's inputs unchanged");
            me.Bounty = 57;
            AllocationMeter.AssertNone(step, Calls, "RefreshLegacyHud with the hp label's inputs unchanged and a bounty of 57");
        }

        [Test]
        public void AClosedDrawerAllocatesNothingOnAnHpChange()
        {
            var canvas = new GameObject("Garbage drawer canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var host = new GameObject("Garbage drawer host");
            try
            {
                var drawer = host.AddComponent<BuildDrawer>();
                drawer.Initialize(canvas.transform);
                var collections = new CollectionsView();
                var me = new SelfView { Id = 1, Hp = 100, MaxHp = 950, Status = ActorStatus.Alive, Weapon = WeaponId.Wand };
                var hps = Enumerable.Range(0, 64).Select(k => 100f + k).ToArray();
                int i = 0;
                Action step = () => { me.Hp = hps[i++ & 63]; drawer.Refresh(collections, me, 1f); };
                AllocationMeter.AssertNone(step, Calls, "BuildDrawer.Refresh on a closed drawer while the owner's hp changes");
                drawer.SetVisible(true);
                Assert.That(AllocationMeter.Allocates(step, Calls), Is.True, "control: an open drawer does build its strings for a new hp");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void TheWeaponNameAllocatesNothing()
        {
            _rig = new HudGarbageRig();
            var readout = HudGarbageRig.Make<Action<SelfView>>(_rig.Hud, "RefreshDamageReadout");
            var me = _rig.Session.Me;
            Action step = () => readout(me);
            AllocationMeter.AssertNone(step, Calls, "RefreshDamageReadout for the same owner");
        }

        [Test]
        public void AnIdleFrameStillAllocatesNothing()
        {
            _rig = new HudGarbageRig();
            _rig.Settle(HudGarbageRig.Kind.Ramp);
            AllocationMeter.AssertNone(_rig.Late, Calls, "an idle HUD frame in the ramp state (nothing delivered)");
        }

        // ------------------------------------------------------------------ 13: an equal summary in a fresh object

        [Test]
        public void AnEqualSummaryInAFreshObjectAllocatesNothingInTheHud()
        {
            _rig = new HudGarbageRig();
            _rig.Settle(HudGarbageRig.Kind.Stand);
            var live = _rig.Session.Collections;
            var summaryField = typeof(CollectionsView).GetField("<Summary>k__BackingField", HudGarbageRig.All);
            var inventoryField = typeof(CollectionsView).GetField("<Inventory>k__BackingField", HudGarbageRig.All);
            Assert.That(summaryField, Is.Not.Null, "set-up: the Summary backing field");
            Assert.That(inventoryField, Is.Not.Null, "set-up: the Inventory backing field");
            var views = new CollectionsView[64];
            for (int k = 0; k < views.Length; k++)
            {
                views[k] = new CollectionsView();
                summaryField.SetValue(views[k], Copy(live.Summary));
                inventoryField.SetValue(views[k], live.Inventory);
            }
            for (int k = 1; k < views.Length; k++)
                Assert.That(ReferenceEquals(views[k].Summary, views[k - 1].Summary) || ReferenceEquals(views[k].Summary.Gains, views[k - 1].Summary.Gains), Is.False,
                    "set-up: every view carries a fresh summary and fresh arrays");
            var refresh = HudGarbageRig.Make<Action<CollectionsView>>(_rig.Hud, "RefreshCollections");
            refresh(views[0]);
            int i = 0;
            Action step = () => refresh(views[i++ & 63]);
            AllocationMeter.AssertNone(step, Calls, "RefreshCollections for a build summary of equal content in a fresh object");
        }

        // ------------------------------------------------------------------ bounded

        [Test]
        public void ARampTickOfTheWholeHudStaysUnderTheBound()
        {
            _rig = new HudGarbageRig();
            _rig.Settle(HudGarbageRig.Kind.Ramp);
            var deliver = _rig.Deliver(_rig.BuildFeed(HudGarbageRig.Kind.Ramp));
            var late = _rig.Late;
            Action tick = () => { deliver(); late(); };
            AllocationMeter.AssertAtMost(tick, Calls, RampTickLimitPerCall * Calls, "a ramp tick (snapshot, build summary State frame, LateUpdate)");
        }

        [Test]
        public void TheCollectionsHandlerInTheRampStaysUnderTheBound()
        {
            _rig = new HudGarbageRig();
            _rig.Settle(HudGarbageRig.Kind.Ramp);
            var deliver = _rig.DeliverStateOnly(_rig.BuildFeed(HudGarbageRig.Kind.Ramp));
            AllocationMeter.AssertAtMost(deliver, Calls, CollectionsLimitPerCall * Calls, "the build summary State frame alone through the HUD-bound session");
        }

        [Test]
        public void ADamageTickOfTheWholeHudStaysUnderTheBound()
        {
            _rig = new HudGarbageRig();
            _rig.Settle(HudGarbageRig.Kind.Damage);
            var deliver = _rig.Deliver(_rig.BuildFeed(HudGarbageRig.Kind.Damage));
            var late = _rig.Late;
            Action tick = () => { deliver(); late(); };
            AllocationMeter.AssertAtMost(tick, Calls, DamageTickLimitPerCall * Calls, "a damage tick (hp falls 7 a snapshot, LateUpdate)");
        }
    }
}
