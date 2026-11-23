using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-SETTINGS-LABELS-1-fix: a scroll frame's position cue shows from the moment its window opens, after a tab change, a text-size
    /// change and a size change, with no scrolling and no hand-raised value-changed event (EditMode never delivers the ScrollRect's
    /// LateUpdate, so nothing here fires one). A "frame" is the rig's canvas update, then the view's own LateUpdate/Update if it has one,
    /// then the canvas update again: the product may refresh on the frame after an event, never more than that.
    /// </summary>
    public sealed class ScrollCueAtOpenEditModeTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly MethodInfo OnEnableMethod = typeof(MythicTextScale).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        const string Controls = "ControlsScroll", Mechanics = "MechanicsScroll";

        // ---------------------------------------------------------------------------------------------------------------- helpers

        static void Invoke(Component view, string method)
        {
            var m = view.GetType().GetMethod(method, Any, null, System.Type.EmptyTypes, null);
            if (m != null) m.Invoke(view, null);
        }

        /// <summary>What play adds on a wake: OnEnable on every active, enabled MythicTextScale under the help root (none runs by itself in EditMode).</summary>
        static void Wake(SettingsRig rig)
        {
            foreach (var m in rig.Help.Root.GetComponentsInChildren<MythicTextScale>(false))
                if (m.isActiveAndEnabled) OnEnableMethod.Invoke(m, null);
        }

        /// <summary>One frame of a real session after an event: lay out, run the views' own per-frame members, lay out again.</summary>
        static void Frame(SettingsRig rig)
        {
            SettingsRig.Update();
            Invoke(rig.Help, "LateUpdate"); Invoke(rig.Help, "Update");
            Invoke(rig.Settings, "LateUpdate"); Invoke(rig.Settings, "Update");
            SettingsRig.Update();
        }

        /// <summary>The event and the frame after it: what the player's frame order gives before the next thing happens.</summary>
        static void Settle(SettingsRig rig) { Frame(rig); Frame(rig); }

        static Transform HelpFrame(SettingsRig rig, string tab) { return rig.Help.Root.Find("HelpGuideCard/" + tab); }
        static Transform SettingsFrame(SettingsRig rig) { return rig.Settings.Root.Find("SettingsScroll"); }

        static void OpenHelp(SettingsRig rig)
        {
            rig.PressHelp();
            Assert.That(rig.Help.IsVisible, Is.True, "the help window is open (set-up)");
            Wake(rig);
            Settle(rig);
        }

        static void ShowTab(SettingsRig rig, string tab)
        {
            if (tab == Controls) rig.Help.ControlsTab.onClick.Invoke(); else rig.Help.MechanicsTab.onClick.Invoke();
            Wake(rig);
            Settle(rig);
        }

        struct Cue { public bool Shown; public float Low, High, Viewport, Content; public bool Exists; }

        static Cue ReadCue(Transform frame)
        {
            var scroll = frame.GetComponent<ScrollRect>();
            var track = frame.Find("ScrollIndicator");
            var cue = new Cue { Viewport = scroll.viewport.rect.height, Content = scroll.content.rect.height, Exists = track != null };
            if (track == null) return cue;
            cue.Shown = track.gameObject.activeSelf;
            var handle = (RectTransform)track.Find("Handle");
            cue.Low = handle.anchorMin.y; cue.High = handle.anchorMax.y;
            return cue;
        }

        /// <summary>The frame is on screen, its content is taller than its viewport (premise), and its cue is shown at the top with length viewport over content.</summary>
        static float AssertCueRight(Transform frame, string what)
        {
            Assert.That(frame.gameObject.activeInHierarchy, Is.True, what + ": the frame is on screen (set-up)");
            var cue = ReadCue(frame);
            Assert.That(cue.Exists, Is.True, what + ": the frame has a ScrollIndicator (set-up)");
            Assert.That(cue.Content, Is.GreaterThan(cue.Viewport + .5f), what + ": the content is taller than its viewport (premise)");
            Assert.That(cue.Shown, Is.True, what + ": the cue is shown");
            float length = cue.Viewport / cue.Content;
            Assert.That(cue.High, Is.EqualTo(1f).Within(.01f), what + ": the handle starts at the top, where the scroll starts");
            Assert.That(cue.High - cue.Low, Is.EqualTo(length).Within(.01f), what + ": the handle is viewport over content long (" + cue.Viewport + " / " + cue.Content + ")");
            return cue.High - cue.Low;
        }

        // ---------------------------------------------------------------------------------------------------------------- 1

        [Test]
        public void TheHelpCueShowsWhenTheWindowOpens([Values(Controls, Mechanics)] string tab, [Values(1f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                OpenHelp(rig);
                if (tab == Mechanics) ShowTab(rig, Mechanics);
                AssertCueRight(HelpFrame(rig, tab), tab + " at text size " + scale);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 2

        [Test]
        public void TheHelpCueFollowsATabChange()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenHelp(rig);
                ShowTab(rig, Mechanics);
                AssertCueRight(HelpFrame(rig, Mechanics), "Mechanics after the tab change");
                ShowTab(rig, Controls);
                AssertCueRight(HelpFrame(rig, Controls), "Controls after the tab back");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 3

        [Test]
        public void TheHelpCueFollowsATextSizeChangeWhileOpen()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenHelp(rig);
                float before = AssertCueRight(HelpFrame(rig, Controls), "Controls at text size 1");
                rig.Own.SetTextScale(1.5f);
                Settle(rig);
                float after = AssertCueRight(HelpFrame(rig, Controls), "Controls after the text size went to 1.5");
                Assert.That(after, Is.LessThan(before - .01f), "the handle is shorter than before: the content grew");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 3b

        [Test]
        public void TheHelpCueFollowsAChangeOfTheWindowSize()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenHelp(rig);
                rig.Help.ApplyViewport(new HudViewport(1f, 1422f, 900f));
                Settle(rig);
                float tall = AssertCueRight(HelpFrame(rig, Controls), "Controls in a 900 high window");
                float tallViewport = ReadCue(HelpFrame(rig, Controls)).Viewport;
                rig.Help.ApplyViewport(new HudViewport(1f, 1422f, 500f));
                Settle(rig);
                float shortViewport = ReadCue(HelpFrame(rig, Controls)).Viewport;
                Assert.That(shortViewport, Is.LessThan(tallViewport - 10f), "the 500 high window has a shorter viewport (set-up)");
                float small = AssertCueRight(HelpFrame(rig, Controls), "Controls in a 500 high window");
                Assert.That(small, Is.LessThan(tall - .01f), "the handle is shorter in the smaller window");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 4 (standing guard)

        /// <summary>A standing guard: it passes before and after the fix. A page whose text fits shows no cue.</summary>
        [Test]
        public void AShortHelpPageShowsNoCue()
        {
            using (var rig = new SettingsRig(1f))
            {
                var body = rig.Help.Root.Find("HelpGuideCard/" + Controls + "/Viewport/Content").GetComponent<TMP_Text>();
                body.text = "Short.";   // built hidden, as in a match; the text is short before the window opens
                OpenHelp(rig);
                var cue = ReadCue(HelpFrame(rig, Controls));
                Assert.That(cue.Exists, Is.True, "the frame has a ScrollIndicator (set-up)");
                Assert.That(cue.Content, Is.LessThanOrEqualTo(cue.Viewport + .5f), "the short text fits its viewport (premise)");
                Assert.That(cue.Shown, Is.False, "a page that fits shows no cue");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 5

        static void OpenSettings(SettingsRig rig, float uiHeight)
        {
            rig.Settings.ApplyViewport(new HudViewport(1f, 1422f, uiHeight));
            rig.Gear();
            Settle(rig);
        }

        [Test]
        public void TheSettingsCueShowsWhenTheCardOpens([Values(1f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                OpenSettings(rig, 800f);
                AssertCueRight(SettingsFrame(rig), "the settings card at text size " + scale);
            }
        }

        [Test]
        public void TheSettingsCueFollowsATextSizeChangeWhileOpen()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenSettings(rig, 600f);
                float before = AssertCueRight(SettingsFrame(rig), "the card at text size 1");
                rig.Own.SetTextScale(1.5f);
                Settle(rig);
                float after = AssertCueRight(SettingsFrame(rig), "the card after the text size went to 1.5");
                Assert.That(after, Is.LessThan(before - .005f), "the handle is shorter than before: the content grew");
            }
        }

        [Test]
        public void TheSettingsCueFollowsAChangeOfTheWindowSize()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenSettings(rig, 600f);
                AssertCueRight(SettingsFrame(rig), "the card in a 600 high window");
                rig.Settings.ApplyViewport(new HudViewport(1f, 1422f, 1600f));
                Settle(rig);
                var fits = ReadCue(SettingsFrame(rig));
                Assert.That(fits.Content, Is.LessThanOrEqualTo(fits.Viewport + .5f), "in a 1600 high window the content fits (premise)");
                Assert.That(fits.Shown, Is.False, "a content that fits shows no cue");
                rig.Settings.ApplyViewport(new HudViewport(1f, 1422f, 600f));
                Settle(rig);
                AssertCueRight(SettingsFrame(rig), "the card in a 600 high window again");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 6

        static void AssertSteady(SettingsRig rig, Transform frame, string what)
        {
            AssertCueRight(frame, what + " has settled with its cue");
            var handle = (RectTransform)frame.Find("ScrollIndicator/Handle");
            var track = (RectTransform)frame.Find("ScrollIndicator");
            Vector2 min = handle.anchorMin, max = handle.anchorMax, offMin = handle.offsetMin, offMax = handle.offsetMax, trackSize = track.sizeDelta;
            for (int i = 0; i < 10; i++) Frame(rig);
            Assert.That(handle.anchorMin, Is.EqualTo(min), what + ": ten more frames move no handle anchor (min)");
            Assert.That(handle.anchorMax, Is.EqualTo(max), what + ": ten more frames move no handle anchor (max)");
            Assert.That(handle.offsetMin, Is.EqualTo(offMin), what + ": nor its offsets (min)");
            Assert.That(handle.offsetMax, Is.EqualTo(offMax), what + ": nor its offsets (max)");
            Assert.That(track.sizeDelta, Is.EqualTo(trackSize), what + ": nor the track");
            Assert.That(track.gameObject.activeSelf, Is.True, what + ": the cue is still shown");
        }

        [Test]
        public void ASteadyFrameRefreshesNothing()
        {
            using (var rig = new SettingsRig(1f))
            {
                OpenHelp(rig);
                AssertSteady(rig, HelpFrame(rig, Controls), "the Help window");
                rig.Gear();
                Settle(rig);
                AssertSteady(rig, SettingsFrame(rig), "the settings card");
                // The views have no per-frame member on the base (checked 2026-11-23: no Update, no LateUpdate). If the fix adds one, it
                // allocates nothing in a steady frame.
                foreach (var view in new Component[] { rig.Help, rig.Settings })
                    foreach (var name in new[] { "LateUpdate", "Update" })
                    {
                        var m = view.GetType().GetMethod(name, Any, null, System.Type.EmptyTypes, null);
                        if (m == null) continue;
                        var step = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), view, m);
                        AllocationMeter.AssertNone(step, 20, view.GetType().Name + "." + name + " in a steady frame");
                    }
            }
        }
    }
}
