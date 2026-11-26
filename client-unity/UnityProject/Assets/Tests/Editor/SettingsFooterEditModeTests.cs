using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-SETTINGS-FOOTER-1: the settings card's footer (Saved line, Reset to defaults, Leave match) is a child of the card itself, stands at the
    /// card's bottom above its 10 unit pad at every scroll position, and the scrolling body ends above it. Reuses the real-HUD rig of the labels packet.
    /// </summary>
    public sealed class SettingsFooterEditModeTests
    {
        const float FooterWithLeave = 62f, FooterWithoutLeave = 36f, BottomPad = 10f;

        // ---------------------------------------------------------------------------------------------------------------- helpers

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static RectTransform Footer(ModernSettingsView view) { return (RectTransform)FindDeep(view.Root, "Footer"); }
        static RectTransform Content(ModernSettingsView view) { return (RectTransform)view.Root.Find("SettingsScroll/Viewport/SettingsContent"); }
        static RectTransform Frame(ModernSettingsView view) { return (RectTransform)view.Root.Find("SettingsScroll"); }
        static ScrollRect Scroll(ModernSettingsView view) { return Frame(view).GetComponent<ScrollRect>(); }

        static UnityEngine.Rect BoundsIn(RectTransform element, Transform relativeTo)
        {
            var corners = new Vector3[4];
            element.GetWorldCorners(corners);
            Vector3 first = relativeTo.InverseTransformPoint(corners[0]);
            float xMin = first.x, xMax = first.x, yMin = first.y, yMax = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 point = relativeTo.InverseTransformPoint(corners[i]);
                xMin = Mathf.Min(xMin, point.x); xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y); yMax = Mathf.Max(yMax, point.y);
            }
            return UnityEngine.Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static string Show(UnityEngine.Rect r) { return "[x " + r.xMin.ToString("0.#") + ".." + r.xMax.ToString("0.#") + ", y " + r.yMin.ToString("0.#") + ".." + r.yMax.ToString("0.#") + "]"; }

        /// <summary>What a frame does after a scroll: the canvas update and the content's layout.</summary>
        static void SetScroll(ModernSettingsView view, float normalized)
        {
            var scroll = Scroll(view);
            scroll.verticalNormalizedPosition = normalized;
            SettingsRig.Update(); LayoutRebuilder.ForceRebuildLayoutImmediate(Content(view)); SettingsRig.Update();
        }

        static void AssertSame(UnityEngine.Rect a, UnityEngine.Rect b, float tolerance, string what)
        {
            Assert.That(Mathf.Abs(a.xMin - b.xMin) <= tolerance && Mathf.Abs(a.xMax - b.xMax) <= tolerance && Mathf.Abs(a.yMin - b.yMin) <= tolerance && Mathf.Abs(a.yMax - b.yMax) <= tolerance,
                what + ": " + Show(a) + " against " + Show(b));
        }

        static bool Overlap(UnityEngine.Rect a, UnityEngine.Rect b, float by)
        {
            return Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > by && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > by;
        }

        /// <summary>The card built alone on a world canvas, as in TheCardAloneIsRightToo (text scale 1), with or without a leave action.</summary>
        sealed class Alone : IDisposable
        {
            readonly SettingsEnv _env; readonly GameObject _canvas, _source, _owner;
            public readonly ModernSettingsView View; public readonly UiDisplayPreferences Prefs; public int Left;

            public Alone(bool withLeave)
            {
                _env = new SettingsEnv(1f);
                _canvas = new GameObject("settings footer alone canvas", typeof(RectTransform), typeof(Canvas));
                _source = new GameObject("settings footer alone source"); _source.SetActive(false);
                _owner = new GameObject("settings footer alone owner");
                _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                ((RectTransform)_canvas.transform).sizeDelta = new Vector2(1920, 1080);
                var source = _source.AddComponent<ShowcaseSettings>();
                SettingsEnv.ScaleField.SetValue(source, 1f);
                View = _owner.AddComponent<ModernSettingsView>();
                Prefs = new UiDisplayPreferences();
                View.Initialize(_canvas.transform, source, null, Prefs, null, withLeave ? (Action)(() => Left++) : null);
                View.SetVisible(true);
                Assert.That(View.IsVisible, Is.True, "the card is open (set-up)");
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_owner); UnityEngine.Object.DestroyImmediate(_canvas); UnityEngine.Object.DestroyImmediate(_source);
                _env.Dispose();
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 1

        [Test]
        public void TheFooterIsInViewOnFirstOpening([Values(.85f, 1f, 1.25f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.Gear();
                SettingsRig.Update();
                var view = rig.Settings; var card = view.Root;
                Assert.That(view.IsVisible, Is.True, "the card is open (set-up)");
                var footer = Footer(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                Assert.That(footer.parent == card, "the Footer is a child of the card itself, not of the scrolling content");
                var viewport = Scroll(view).viewport;
                var inside = card.rect;
                foreach (var name in new[] { "Reset", "Leave" })
                {
                    var t = FindDeep(card, name);
                    Assert.That(t != null, name + " exists (set-up)");
                    Assert.That(t.IsChildOf(viewport), Is.False, name + " is not under the scroll's viewport, so no mask cuts it");
                    var b = BoundsIn((RectTransform)t, card);
                    Assert.That(b.xMin >= inside.xMin - .2f && b.xMax <= inside.xMax + .2f, name + " lies inside the card sideways: " + Show(b) + " in " + Show(inside));
                    Assert.That(b.yMax <= inside.yMax + .2f, name + " lies below the card's top: " + Show(b) + " in " + Show(inside));
                    Assert.That(b.yMin >= inside.yMin + BottomPad - .5f, name + " lies above the card's bottom pad: " + Show(b) + " in " + Show(inside));
                }
                var saved = FindDeep(card, "Label");
                foreach (var t in card.GetComponentsInChildren<TMP_Text>(true)) if (t.text == "Saved in this browser.") saved = t.transform;
                Assert.That(saved.gameObject.activeInHierarchy, Is.True, "the Saved label is active");
                var sb = BoundsIn((RectTransform)saved, card);
                Assert.That(sb.yMin >= inside.yMin + BottomPad - 8f && sb.yMax <= inside.yMax, "the Saved label is inside the card: " + Show(sb) + " in " + Show(inside));
                Assert.That(saved.IsChildOf(viewport), Is.False, "the Saved label is not under the viewport");
                var fb = BoundsIn(footer, card);
                Assert.That(Mathf.Abs(fb.yMin - (inside.yMin + BottomPad)) <= .5f, "the Footer's lower edge is the card's bottom pad (10) above the card's bottom: " + Show(fb) + " in " + Show(inside));
                Assert.That(Mathf.Abs(fb.xMin - (inside.xMin + ModernSettingsView.CardPadSide)) <= .5f && Mathf.Abs(fb.xMax - (inside.xMax - ModernSettingsView.CardPadSide)) <= .5f,
                    "the Footer keeps the side padding of the rows: " + Show(fb) + " in " + Show(inside));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 2

        [Test]
        public void TheFooterStaysWhileTheBodyScrolls([Values(1f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.Gear();
                SettingsRig.Update();
                var view = rig.Settings; var card = view.Root;
                var scroll = Scroll(view); var footer = Footer(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                Assert.That(Content(view).rect.height, Is.GreaterThan(scroll.viewport.rect.height), "the content is taller than the viewport (set-up)");
                var rest = BoundsIn(footer, card);
                foreach (float at in new[] { 1f, .5f, 0f })
                {
                    SetScroll(view, at);
                    AssertSame(BoundsIn(footer, card), rest, .2f, "scrolled to " + at + " the footer's rect in the card is the one at the start");
                }
                // At the bottom the last row is wholly in the viewport, and the footer is not covering it.
                SetScroll(view, 0f);
                RectTransform row = null;
                foreach (var t in Content(view).GetComponentsInChildren<TMP_Text>(true)) if (t.text == "Training walkthrough") row = (RectTransform)t.transform.parent;
                Assert.That(row != null, "the Training walkthrough row exists (set-up)");
                var view2 = scroll.viewport.rect; var rb = BoundsIn(row, scroll.viewport);
                Assert.That(rb.yMin >= view2.yMin - .2f && rb.yMax <= view2.yMax + .2f, "at the bottom the Training walkthrough row lies wholly in the viewport: " + Show(rb) + " in " + Show(view2));
                var replay = BoundsIn((RectTransform)FindDeep(row, "ReplayTutorial"), scroll.viewport);
                Assert.That(replay.yMin >= view2.yMin - .2f && replay.yMax <= view2.yMax + .2f, "at the bottom the Replay tutorial button lies wholly in the viewport: " + Show(replay) + " in " + Show(view2));
                float gap = BoundsIn(row, card).yMin - rest.yMax;
                Assert.That(gap >= -.5f && gap <= 5f, "at the bottom the last row ends just above the footer (the content's 4 unit pad between): gap " + gap + ", row " + Show(BoundsIn(row, card)) + ", footer " + Show(rest));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 3

        [Test]
        public void TheBodyEndsAboveTheFooter()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.Gear();
                SettingsRig.Update();
                var view = rig.Settings; var card = view.Root;
                var footer = Footer(view); var scroll = Scroll(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                var fb = BoundsIn(footer, card);
                var frame = BoundsIn(Frame(view), card);
                Assert.That(Mathf.Abs(frame.yMin - fb.yMax) <= .5f, "the scroll frame's bottom edge is the footer's top edge: frame " + Show(frame) + ", footer " + Show(fb));
                var viewportBounds = BoundsIn(scroll.viewport, card);
                Assert.That(viewportBounds.yMin >= fb.yMax - .5f, "the viewport lies above the footer: viewport " + Show(viewportBounds) + ", footer " + Show(fb));
                var meets = new List<string>();
                foreach (float at in new[] { 1f, .5f, 0f })
                {
                    SetScroll(view, at);
                    var content = Content(view);
                    for (int i = 0; i < content.childCount; i++)
                    {
                        var row = (RectTransform)content.GetChild(i);
                        if (row == footer) continue;
                        var b = BoundsIn(row, card);
                        var clipped = UnityEngine.Rect.MinMaxRect(Mathf.Max(b.xMin, viewportBounds.xMin), Mathf.Max(b.yMin, viewportBounds.yMin), Mathf.Min(b.xMax, viewportBounds.xMax), Mathf.Min(b.yMax, viewportBounds.yMax));
                        if (clipped.width > 0 && clipped.height > 0 && Overlap(clipped, BoundsIn(footer, card), .5f)) meets.Add("row " + i + " at " + at);
                    }
                }
                Assert.That(meets.Count, Is.EqualTo(0), "no row, clipped to the viewport, meets the footer: " + string.Join("; ", meets));

                var rule = footer.Find("Rule") as RectTransform;
                Assert.That(rule != null, "the Footer has a Rule line (a child named Rule, as the rows have)");
                if (rule == null) return;
                var rb = BoundsIn(rule, card);
                Assert.That(Mathf.Abs(rb.yMax - fb.yMax) <= .5f, "the rule's top is the footer's top edge: rule " + Show(rb) + ", footer " + Show(fb));
                Assert.That(rb.height >= .5f && rb.height <= 1.5f, "the rule is a hairline, 1 unit high: " + rb.height);
                Assert.That(Mathf.Abs(rb.xMin - fb.xMin) <= .5f && Mathf.Abs(rb.xMax - fb.xMax) <= .5f, "the rule spans the footer's width, as a row's rule spans its row: rule " + Show(rb) + ", footer " + Show(fb));
                var asRows = FindDeep(Content(view), "Rule").GetComponent<Graphic>().color;
                var color = rule.GetComponent<Graphic>().color;
                Assert.That(Mathf.Abs(color.r - asRows.r) < .01f && Mathf.Abs(color.g - asRows.g) < .01f && Mathf.Abs(color.b - asRows.b) < .01f && Mathf.Abs(color.a - asRows.a) < .01f,
                    "the rule has the look of the rows' rules: " + color + " against " + asRows);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 4

        [Test]
        public void ATallWindowShowsTheWholeCardWithoutScrolling()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.Gear();
                SettingsRig.Update();
                var view = rig.Settings; var card = view.Root;
                float tall = Content(view).rect.height + 300f;   // uiH: the cap, uiH - 60, is the content plus 240 at least
                view.ApplyViewport(new HudViewport(1f, 1422f, tall));
                SettingsRig.Update();
                var content = Content(view); var footer = Footer(view); var scroll = Scroll(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                Assert.That(Mathf.Abs(footer.rect.height - FooterWithLeave) <= .01f, "the footer is 62 high with a leave action: " + footer.rect.height);
                float want = ModernSettingsView.HeaderHeight + content.rect.height + FooterWithLeave + BottomPad;
                Assert.That(card.sizeDelta.y, Is.EqualTo(want).Within(.5f), "the card is header + content + footer + 10 high (content " + content.rect.height + ")");
                Assert.That(card.sizeDelta.y, Is.LessThan(tall - 60f - 1f), "the card is below its cap (set-up)");
                Assert.That(content.rect.height, Is.LessThanOrEqualTo(scroll.viewport.rect.height + .5f), "nothing scrolls: the content is not taller than the viewport");
                var fb = BoundsIn(footer, card);
                Assert.That(Mathf.Abs(fb.yMin - (card.rect.yMin + BottomPad)) <= .5f, "the footer stands at the card's bottom pad: " + Show(fb) + " in " + Show(card.rect));
                var lower = new List<string>();
                for (int i = 0; i < content.childCount; i++)
                {
                    var row = (RectTransform)content.GetChild(i);
                    if (row == footer) continue;
                    var b = BoundsIn(row, card);
                    if (b.yMin < fb.yMax - .5f) lower.Add("row " + i + " " + Show(b));
                }
                Assert.That(lower.Count, Is.EqualTo(0), "the footer is the lowest thing in the card, every row lies above it: " + string.Join("; ", lower));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 4b

        /// <summary>P-UI-GUARDS-2: a short window (1280 x 400, the card capped at 340) keeps the footer, with its Reset and Leave, whole inside the card
        /// above the scroll frame's bottom edge, while the body scrolls to both ends. (ModernSettingsLayoutEditModeTests.ShortWindowScrollsTheBodyToBothEnds
        /// now reads only the last scrolling row, since the footer left the scrolling content.)</summary>
        [Test]
        public void AShortWindowKeepsTheFooterInView()
        {
            using (var alone = new Alone(true))
            {
                var view = alone.View; var card = view.Root;
                view.ApplyViewport(new HudViewport(1f, 1280f, 400f));
                SettingsRig.Update();
                var content = Content(view); var scroll = Scroll(view); var footer = Footer(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                Assert.That(card.sizeDelta.y, Is.EqualTo(340f).Within(.01f), "the card is capped at uiH 400 - 60");
                Assert.That(footer.parent == card, "the Footer is a child of the card itself, not of the scrolling content");
                Assert.That(Mathf.Abs(footer.rect.height - FooterWithLeave) <= .01f, "the footer is 62 high with a leave action: " + footer.rect.height);
                var inside = card.rect;
                var fb = BoundsIn(footer, card);
                foreach (var name in new[] { "Reset", "Leave" })
                {
                    var t = FindDeep(card, name);
                    Assert.That(t != null, name + " exists (set-up)");
                    Assert.That(t.gameObject.activeInHierarchy, Is.True, name + " is active");
                    var b = BoundsIn((RectTransform)t, card);
                    Assert.That(b.xMin >= inside.xMin - .2f && b.xMax <= inside.xMax + .2f, name + " lies inside the card sideways: " + Show(b) + " in " + Show(inside));
                    Assert.That(b.yMax <= inside.yMax + .2f, name + " lies below the card's top: " + Show(b) + " in " + Show(inside));
                    Assert.That(b.yMin >= inside.yMin + BottomPad - .5f, name + " lies above the card's bottom pad: " + Show(b) + " in " + Show(inside));
                }
                Assert.That(fb.yMin >= inside.yMin + BottomPad - .5f && fb.yMax <= inside.yMax + .2f, "the Footer lies inside the card above its bottom pad: " + Show(fb) + " in " + Show(inside));
                var frame = BoundsIn(Frame(view), card);
                Assert.That(frame.yMin >= fb.yMax - .5f, "the scroll frame ends at or above the footer's top: frame " + Show(frame) + ", footer " + Show(fb));
                var viewport = scroll.viewport;
                Assert.That(viewport.rect.height, Is.EqualTo(232f).Within(1f), "the viewport is about 232 high");
                Assert.That(content.rect.height, Is.GreaterThan(viewport.rect.height), "the body overflows, so it scrolls");
                var viewportBox = viewport.rect;
                var lastRow = (RectTransform)content.GetChild(content.childCount - 1);
                Assert.That(lastRow != footer, "the footer is not inside the scrolling content");
                SetScroll(view, 0f);
                var lb = BoundsIn(lastRow, viewport);
                Assert.That(lb.xMin >= viewportBox.xMin - .5f && lb.xMax <= viewportBox.xMax + .5f && lb.yMin >= viewportBox.yMin - .5f && lb.yMax <= viewportBox.yMax + .5f,
                    "at the bottom of the scroll the Training walkthrough row lies wholly in the viewport: " + Show(lb) + " in " + Show(viewportBox));
                fb = BoundsIn(footer, card);
                Assert.That(Mathf.Abs(fb.yMin - (inside.yMin + BottomPad)) <= .5f, "and the footer has not moved: " + Show(fb) + " in " + Show(inside));
                SetScroll(view, 1f);
                fb = BoundsIn(footer, card);
                Assert.That(Mathf.Abs(fb.yMin - (inside.yMin + BottomPad)) <= .5f, "at the top the footer stands at the same place: " + Show(fb) + " in " + Show(inside));
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 5

        [Test]
        public void ACardWithoutALeaveActionHasTheShortFooter()
        {
            using (var alone = new Alone(false))
            {
                var view = alone.View; var card = view.Root;
                SettingsRig.Update();
                var footer = Footer(view);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                var reset = (RectTransform)FindDeep(card, "Reset");
                Assert.That(reset != null, "Reset exists (set-up)");
                var rb = BoundsIn(reset, card); var inside = card.rect;
                Assert.That(rb.yMin >= inside.yMin + BottomPad - .5f && rb.yMax <= inside.yMax && rb.xMin >= inside.xMin - .2f && rb.xMax <= inside.xMax + .2f,
                    "Reset is in view, inside the card above its bottom pad: " + Show(rb) + " in " + Show(inside));
                Assert.That(footer.parent == card, "the Footer is a child of the card itself");
                Assert.That(Mathf.Abs(footer.rect.height - FooterWithoutLeave) <= .01f, "the footer is 36 high without a leave action: " + footer.rect.height);
                Assert.That(FindDeep(card, "Leave") == null, "no Leave object exists without a leave action");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- guards (green before and after)

        [Test]
        public void TheFootersThreeTextsAndPlacesInsideItAreUnchanged()
        {
            using (var alone = new Alone(true))
            {
                var footer = Footer(alone.View);
                Assert.That(footer != null, "the card has a Footer (set-up)");
                Assert.That(Mathf.Abs(footer.rect.height - FooterWithLeave) <= .01f, "62 high with a leave action: " + footer.rect.height);
                var saved = footer.Find("Saved/Label"); var reset = footer.Find("Reset"); var leave = footer.Find("Leave");
                Assert.That(saved != null && reset != null && leave != null, "Saved/Label, Reset and Leave are children of the Footer");
                Assert.That(saved.GetComponent<TMP_Text>().text, Is.EqualTo("Saved in this browser."));
                Assert.That(reset.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Reset to defaults"));
                Assert.That(leave.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Leave match"));
                var inner = footer.rect;
                var savedBox = BoundsIn((RectTransform)footer.Find("Saved"), footer); var r = BoundsIn((RectTransform)reset, footer); var l = BoundsIn((RectTransform)leave, footer);
                Assert.That(Mathf.Abs(savedBox.xMin - inner.xMin) <= .2f && Mathf.Abs(savedBox.yMax - (inner.yMax - 8f)) <= .2f && Mathf.Abs(savedBox.width - 150f) <= .2f && Mathf.Abs(savedBox.height - 14f) <= .2f, "Saved stands at (0, 8), 150 x 14: " + Show(savedBox));
                Assert.That(Mathf.Abs(r.xMax - inner.xMax) <= .2f && Mathf.Abs(r.yMax - (inner.yMax - 4f)) <= .2f && Mathf.Abs(r.width - 120f) <= .2f && Mathf.Abs(r.height - 22f) <= .2f, "Reset stands at the top right, 4 down, 120 x 22: " + Show(r));
                Assert.That(Mathf.Abs(l.xMin - inner.xMin) <= .2f && Mathf.Abs(l.yMax - (inner.yMax - 34f)) <= .2f && Mathf.Abs(l.width - 140f) <= .2f && Mathf.Abs(l.height - 22f) <= .2f, "Leave stands at (0, 34), 140 x 22: " + Show(l));
            }
        }

        [Test]
        public void LeaveNeedsASecondClickAndResetResetsTheDisplayOptions()
        {
            string[] ints = { "rh.ui.map.toggle", "rh.ui.route.visible", "rh.ui.showFps", "rh.ui.showPing", "rh.ui.quickCast" };
            const string scaleKey = "rh.ui.minimapScale.v2";
            var had = new bool[ints.Length]; var saved = new int[ints.Length];
            for (int i = 0; i < ints.Length; i++) { had[i] = PlayerPrefs.HasKey(ints[i]); if (had[i]) saved[i] = PlayerPrefs.GetInt(ints[i]); }
            bool hadScale = PlayerPrefs.HasKey(scaleKey); float savedScale = hadScale ? PlayerPrefs.GetFloat(scaleKey) : 0f;
            try
            {
                using (var alone = new Alone(true))
                {
                    var card = alone.View.Root;
                    var leave = FindDeep(card, "Leave").GetComponent<Button>(); var reset = FindDeep(card, "Reset").GetComponent<Button>();
                    var label = leave.GetComponentInChildren<TMP_Text>();
                    leave.onClick.Invoke();
                    Assert.That(alone.Left, Is.EqualTo(0), "the first click does not leave");
                    Assert.That(label.text, Is.EqualTo("Click again to leave"), "the first click arms the button");
                    Assert.That(alone.View.IsVisible, Is.True, "the card stays open after the first click");
                    leave.onClick.Invoke();
                    Assert.That(alone.Left, Is.EqualTo(1), "the second click, within the time, leaves");
                    Assert.That(alone.View.IsVisible, Is.False, "the card closes as it leaves");
                    alone.View.SetVisible(true);
                    Assert.That(label.text, Is.EqualTo("Leave match"), "the label reads Leave match again once the card reopens");

                    alone.Prefs.SetShowFps(true); alone.Prefs.SetShowPing(false); alone.Prefs.SetQuickCast(true); alone.Prefs.SetMinimapScale(UiDisplayPreferences.MinimapScales[0]);
                    Assert.That(alone.Prefs.ShowFps && !alone.Prefs.ShowPing && alone.Prefs.QuickCast, "the options are away from their defaults (set-up)");
                    reset.onClick.Invoke();
                    Assert.That(alone.Prefs.ShowFps, Is.False, "Reset turns Show FPS off");
                    Assert.That(alone.Prefs.ShowPing, Is.True, "Reset turns Show ping on");
                    Assert.That(alone.Prefs.QuickCast, Is.False, "Reset turns Quick cast off");
                    Assert.That(alone.Prefs.MinimapScale, Is.EqualTo(1f).Within(.0001f), "Reset puts the minimap size back to 100%");
                }
            }
            finally
            {
                for (int i = 0; i < ints.Length; i++) { if (had[i]) PlayerPrefs.SetInt(ints[i], saved[i]); else PlayerPrefs.DeleteKey(ints[i]); }
                if (hadScale) PlayerPrefs.SetFloat(scaleKey, savedScale); else PlayerPrefs.DeleteKey(scaleKey);
            }
        }

        [Test]
        public void TheTabCycleEndsAtResetThenLeave()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.Gear();
                SettingsRig.Update();
                var card = rig.Settings.Root;
                EventSystem.current.SetSelectedGameObject(null);
                Assert.That(UiFocusNavigation.Cycle(card, true), Is.True, "a backwards cycle from nothing selects a control");
                var last = EventSystem.current.currentSelectedGameObject;
                Assert.That(UiFocusNavigation.Cycle(card, true), Is.True, "and again");
                var beforeLast = EventSystem.current.currentSelectedGameObject;
                Assert.That(last != null && last.name == "Leave", "the last stop of the cycle is Leave: " + (last == null ? "none" : last.name));
                Assert.That(beforeLast != null && beforeLast.name == "Reset", "the one before is Reset: " + (beforeLast == null ? "none" : beforeLast.name));
                Assert.That(UiFocusNavigation.Cycle(card, false), Is.True, "forwards from Reset");
                Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("Leave"), "forwards from Reset reaches Leave");
            }
        }
    }
}
