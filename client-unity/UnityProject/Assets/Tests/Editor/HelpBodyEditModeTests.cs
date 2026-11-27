using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-SETTINGS-LABELS-1: the help window's two bodies stand at their wanted size in the order of a real match (the window is built
    /// hidden, a Changed event may run, then H opens it), scroll where their text is long, follow a text-size change, and show where they scroll.
    /// </summary>
    public sealed class HelpBodyEditModeTests
    {
        const float ExpectedBodySize = 16f;
        const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo OnEnableMethod = typeof(MythicTextScale).GetMethod("OnEnable", NP);
        static readonly string[] Tabs = { "ControlsScroll", "MechanicsScroll" };

        /// <summary>What play adds on a wake: OnEnable on every active, enabled MythicTextScale under the help root (none runs by itself in EditMode), then the canvas update.</summary>
        static void WakeAndUpdate(HelpGuideView help)
        {
            foreach (var m in help.Root.GetComponentsInChildren<MythicTextScale>(false))
                if (m.isActiveAndEnabled) OnEnableMethod.Invoke(m, null);
            SettingsRig.Update();
        }

        static TMP_Text Body(HelpGuideView help, string tab)
        {
            var t = help.Root.Find("HelpGuideCard/" + tab + "/Viewport/Content");
            Assert.That(t != null, tab + " body exists (set-up)");
            return t.GetComponent<TMP_Text>();
        }

        static void Show(SettingsRig rig, string tab)
        {
            if (tab == "ControlsScroll") rig.Help.ControlsTab.onClick.Invoke(); else rig.Help.MechanicsTab.onClick.Invoke();
            WakeAndUpdate(rig.Help);
        }

        static ScrollRect ScrollOf(HelpGuideView help, string tab) { return help.Root.Find("HelpGuideCard/" + tab).GetComponent<ScrollRect>(); }

        static void AssertWanted(SettingsRig rig, string tab, float scale, string at)
        {
            var body = Body(rig.Help, tab);
            Assert.That(body.gameObject.activeInHierarchy, Is.True, tab + " body is active in the hierarchy (set-up) " + at);
            Assert.That(body.isOrthographic, Is.True, tab + " body has woken (set-up) " + at);
            Assert.That(body.fontSize, Is.EqualTo(ExpectedBodySize * Mathf.Clamp(scale, .85f, 1.5f)).Within(.01f), tab + " body stands at base size x text scale " + at);
        }

        // ---------------------------------------------------------------------------------------------------------------- 9

        [Test]
        public void TheHelpBodyTakesItsWantedSizeInTheRealOrder([Values(.85f, 1f, 1.25f, 1.5f)] float scale, [Values(false, true)] bool extraStep)
        {
            using (var rig = new SettingsRig(scale))
            {
                if (extraStep) rig.FireStep();
                Assert.That(rig.Changes, Is.EqualTo(extraStep ? 1 : 0), "the test fired exactly the Changed events it means to before opening the help (set-up)");
                rig.PressHelp();
                Assert.That(rig.Help.IsVisible, Is.True, "the help window is open (set-up)");
                WakeAndUpdate(rig.Help);
                AssertWanted(rig, "ControlsScroll", scale, "on the first opening");
                Show(rig, "MechanicsScroll");
                AssertWanted(rig, "MechanicsScroll", scale, "after the Mechanics tab");
                rig.PressHelp(); rig.PressHelp();
                Assert.That(rig.Help.IsVisible, Is.True, "the help window is open again (set-up)");
                WakeAndUpdate(rig.Help);
                AssertWanted(rig, "MechanicsScroll", scale, "after closing and opening");
                Show(rig, "ControlsScroll");
                AssertWanted(rig, "ControlsScroll", scale, "after the Controls tab again");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 10

        [Test]
        public void TheHelpBodyScrollsWhereItsTextIsLong([Values(.85f, 1f, 1.25f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.PressHelp();
                WakeAndUpdate(rig.Help);
                foreach (var tab in Tabs)
                {
                    if (tab == "MechanicsScroll") Show(rig, tab);
                    var body = Body(rig.Help, tab); var scroll = ScrollOf(rig.Help, tab);
                    float margin = ScrollIndicator.Width + ScrollIndicator.Inset + 1f;
                    Assert.That(body.margin.z, Is.EqualTo(margin).Within(.01f), tab + ": no line ends under the position cue");
                    float wanted = ExpectedBodySize * Mathf.Clamp(scale, .85f, 1.5f);
                    float keep = body.fontSize;
                    body.fontSize = wanted;
                    float preferred = body.GetPreferredValues(body.text, body.rectTransform.rect.width - body.margin.z, 0f).y;
                    body.fontSize = keep;
                    Assert.That(body.rectTransform.rect.height, Is.GreaterThan(scroll.viewport.rect.height), tab + ": the text is taller than its pane, so it scrolls");
                    Assert.That(body.rectTransform.rect.height, Is.EqualTo(preferred).Within(1f), tab + ": the box is as tall as the text at its wanted size and wrap width");
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 11

        [Test]
        public void AHelpBodyFollowsATextSizeChangeWhileOpen()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.PressHelp();
                WakeAndUpdate(rig.Help);
                var body = Body(rig.Help, "ControlsScroll");
                float h1 = body.rectTransform.rect.height;
                Assert.That(body.fontSize, Is.EqualTo(ExpectedBodySize).Within(.01f), "the body uses the readable base size on opening");
                rig.Own.SetTextScale(1.5f);
                SettingsRig.Update();
                Assert.That(body.fontSize, Is.EqualTo(ExpectedBodySize * 1.5f).Within(.01f), "the open body follows the enlarged text size");
                Assert.That(body.rectTransform.rect.height, Is.GreaterThan(h1), "and its box grows with the text");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 15

        [Test]
        public void TheHelpPanesShowTheirPositionCue()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.PressHelp();
                WakeAndUpdate(rig.Help);
                foreach (var tab in Tabs)
                {
                    if (tab == "MechanicsScroll") Show(rig, tab);
                    var scroll = ScrollOf(rig.Help, tab);
                    Assert.That(Body(rig.Help, tab).rectTransform.rect.height, Is.GreaterThan(scroll.viewport.rect.height), tab + ": the text is taller than its pane (set-up)");
                    var track = rig.Help.Root.Find("HelpGuideCard/" + tab + "/ScrollIndicator");
                    Assert.That(track != null, tab + " has a ScrollIndicator on its frame");
                    if (track == null) continue;
                    scroll.onValueChanged.Invoke(scroll.normalizedPosition);   // EditMode never delivers the ScrollRect's LateUpdate
                    Assert.That(track.gameObject.activeSelf, Is.True, tab + ": a taller text shows the cue");
                }
            }
        }
    }
}
