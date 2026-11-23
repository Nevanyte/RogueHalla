using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// What a test of the settings card and the help window needs: the pre-made ShowcaseSettings (reused, or made and woken), its saved text
    /// scale and four PlayerPrefs, the static Quality and the current EventSystem, all restored on Dispose.
    /// </summary>
    internal class SettingsEnv : IDisposable
    {
        internal const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", NP);
        static readonly FieldInfo ChangedField = typeof(ShowcaseSettings).GetField("Changed", NP);
        static readonly string[] PrefKeys = { "roguehalla.showcase.textScale", "roguehalla.showcase.music", "roguehalla.showcase.ambience", "roguehalla.showcase.sfx" };

        public readonly ShowcaseSettings Current;
        readonly GameObject _createdGo; readonly float _savedScale; readonly UiTheme _savedTheme;
        readonly bool[] _had = new bool[4]; readonly float[] _saved = new float[4];
        readonly EventSystem _priorEvents;

        public SettingsEnv(float scale)
        {
            _savedTheme = UiTheme.Current;
            _priorEvents = EventSystem.current;
            Current = ShowcaseSettings.Current;
            if (Current == null)
            {
                _createdGo = new GameObject("settings labels settings");
                Current = _createdGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", NP).Invoke(Current, null);
            }
            _savedScale = (float)ScaleField.GetValue(Current);
            for (int i = 0; i < PrefKeys.Length; i++)
            {
                _had[i] = PlayerPrefs.HasKey(PrefKeys[i]);
                if (_had[i]) _saved[i] = PlayerPrefs.GetFloat(PrefKeys[i]);
            }
            ScaleField.SetValue(Current, scale);
        }

        /// <summary>What ShowcaseSettings does after any change: raise Changed (no PlayerPrefs write).</summary>
        public static void RaiseChanged(ShowcaseSettings settings)
        {
            var changed = (Action)ChangedField.GetValue(settings);
            if (changed != null) changed();
        }

        public virtual void Dispose()
        {
            for (int i = 0; i < PrefKeys.Length; i++)
            {
                if (_had[i]) PlayerPrefs.SetFloat(PrefKeys[i], _saved[i]); else PlayerPrefs.DeleteKey(PrefKeys[i]);
            }
            if (Current != null) ScaleField.SetValue(Current, _savedScale);
            if (_createdGo != null) UnityEngine.Object.DestroyImmediate(_createdGo);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            UiTheme.Current = _savedTheme;
            Quality.Reset();
        }
    }

    /// <summary>
    /// The real HUD built through the real Initialize with a text scale set the way the saved setting would be, in the order of a real match:
    /// the settings card and the help window are built hidden, a Changed event may run, then the player opens one.
    /// </summary>
    internal sealed class SettingsRig : SettingsEnv
    {
        sealed class HudTransport : ITransport
        {
            public event Action Opened; public event Action<byte[]> Received; public event Action<string> Closed;
            public void Connect(string url) { } public void Send(byte[] frame) { } public void Close() { } public void Poll() { }
            public void Open() { if (Opened != null) Opened(); }
            public void Deliver(ServerMsg message) { if (Received != null) Received(message.Encode()); }
        }

        public static readonly HudViewport Wide = new HudViewport(1f, 1422f, 800f);

        public readonly MatchClient Client; public readonly MatchHud Hud; public readonly ModernSettingsView Settings; public readonly HelpGuideView Help;
        public readonly ShowcaseSettings Own; public readonly float Scale;
        /// <summary>The Changed events Own raised since Build ended.</summary>
        public int Changes;
        readonly GameObject _root, _events;

        public SettingsRig(float scale) : base(scale)
        {
            Scale = scale;
            _events = new GameObject("settings labels events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("settings labels hud fixture");
            Client = _root.AddComponent<MatchClient>(); Client.enabled = false;
            var transport = new HudTransport();
            var session = new Session(transport, new SessionOptions { Url = "ws://flows/ws", Profile = "flows", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", NP).Invoke(Client, new object[] { session });
            Hud = Client.Hud;
            Own = (ShowcaseSettings)typeof(MatchHud).GetField("_showcaseSettings", NP).GetValue(Hud);
            Settings = Client.GetComponent<ModernSettingsView>();
            Help = Client.GetComponent<HelpGuideView>();
            session.Connect(); transport.Open();
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            transport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = 1, Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            // In play the HUD's own component has woken and holds the saved scale; in EditMode it has not (its scale is 0 and the Text size slider's
            // echo at the first canvas update sets .85). Build ends as a real match does: one Changed event at the test's scale has run.
            ScaleField.SetValue(Own, scale);
            RaiseChanged(Own);
            Settings.ApplyViewport(Wide);
            Assert.That(Own.TextScale, Is.EqualTo(scale).Within(.0001f), "the rig ends with the HUD's own text scale at the test's scale (set-up)");
            Own.Changed += CountChange;
        }

        void CountChange() { Changes++; }

        /// <summary>What ShowcaseSettings.Update does when the adaptive controller moves the preset: one more Changed event.</summary>
        public void FireStep()
        {
            var to = Own.EffectiveQuality == ShowcaseQuality.Ultra ? ShowcaseQuality.High : ShowcaseQuality.Ultra;
            typeof(ShowcaseSettings).GetMethod("SetEffective", NP).Invoke(Own, new object[] { to });
        }

        Button Chrome(string name)
        {
            var t = Client.transform.Find("MatchUI/ChromeStage/" + name);
            Assert.That(t != null, "the chrome button " + name + " exists (set-up)");
            return t.GetComponent<Button>();
        }

        /// <summary>The gear button: ToggleSettings.</summary>
        public void Gear() { Chrome("Settings").onClick.Invoke(); }
        /// <summary>The help button (H calls the same): ToggleHelp.</summary>
        public void PressHelp() { Chrome("Help").onClick.Invoke(); }

        /// <summary>What a frame does after a button press: the canvas update (layout), twice.</summary>
        public static void Update() { Canvas.ForceUpdateCanvases(); Canvas.ForceUpdateCanvases(); }

        public override void Dispose()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            base.Dispose();
        }
    }

    /// <summary>
    /// P-UI-SETTINGS-LABELS-1: the settings card's labels and notes stand at their real size from the first opening, a note wraps under its
    /// label with the switch on the label line, the open card follows a text-size change, and the card shows where it scrolls.
    /// "Right" is derived from the awake label (a refit on its laid-out box, or base size x text scale), never typed.
    /// </summary>
    public sealed class SettingsLabelsEditModeTests
    {
        static readonly float[] Scales = { .85f, 1f, 1.25f, 1.5f };
        static readonly string[] NoteRows = { "Fullscreen", "Tab toggles map", "Quick cast" };
        /// <summary>GC.Alloc samples of 20 calls of Settings.ApplyViewport(same viewport) on a settled card, from the reference cure (the least of three runs of AllocationMeter.Samples, 2026-11-23: 0, 0, 0; the settled card on the base also gives 0, 0, 0).</summary>
        const int FitBaseSamples = 0;

        // ---------------------------------------------------------------------------------------------------------------- helpers

        static float Wanted(float baseSize, float scale) { return baseSize * Mathf.Clamp(scale, .85f, 1.5f); }

        static TMP_Text LabelWithText(Transform root, string text)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) if (t.text == text) return t;
            return null;
        }

        static TMP_Text LabelStartingWith(Transform root, string text)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) if (t.text != null && t.text.StartsWith(text, StringComparison.Ordinal)) return t;
            return null;
        }

        static RectTransform Content(ModernSettingsView view) { return (RectTransform)view.Root.Find("SettingsScroll/Viewport/SettingsContent"); }
        static ScrollRect Scroll(ModernSettingsView view) { return view.Root.Find("SettingsScroll").GetComponent<ScrollRect>(); }

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

        static string Cut(string s, int n) { s = (s ?? "").Replace("\n", " "); return s.Length <= n ? s : s.Substring(0, n - 1) + "~"; }

        /// <summary>
        /// Every fitted label under the card is awake and at the size a refit on its laid-out box gives; the six labels of the three note rows
        /// are not fitted and stand at base size x text scale; with direct (card 320 wide, scale at most 1) five named labels equal that size too.
        /// </summary>
        internal static void AssertCardRight(ModernSettingsView view, float scale, bool direct)
        {
            var all = view.Root.GetComponentsInChildren<MythicTextScale>(true);
            var fitted = new List<MythicTextScale>();
            foreach (var m in all) if (m.FitBox) fitted.Add(m);
            var asleep = new List<string>();
            var before = new float[fitted.Count];
            for (int i = 0; i < fitted.Count; i++)
            {
                var t = fitted[i].GetComponent<TMP_Text>();
                before[i] = t.fontSize;
                if (!t.gameObject.activeInHierarchy || !t.isOrthographic) asleep.Add(Cut(t.text, 24));
            }
            Assert.That(asleep.Count, Is.EqualTo(0), "every fitted label is active in the hierarchy and has woken, so its measure is true (set-up): " + string.Join("; ", asleep));
            Assert.That(fitted.Count, Is.GreaterThanOrEqualTo(30), "at least 30 fitted labels under the card (set-up)");

            // The six Unfit labels, and the five named labels, are read BEFORE any refit below can heal them.
            var wrong = new List<string>();
            int notRows = 0;
            foreach (var row in view.Root.GetComponentsInChildren<Transform>(true))
            {
                if (row.name != "SettingRow" || row.Find("SettingNote") == null) continue;
                notRows++;
                foreach (var name in new[] { "SettingLabel", "SettingNote" })
                {
                    var t = row.Find(name).GetComponent<TMP_Text>(); var m = t.GetComponent<MythicTextScale>();
                    float want = Wanted(m.BaseSize, scale);
                    if (m.FitBox || Mathf.Abs(t.fontSize - want) > .01f)
                        wrong.Add(name + " \"" + Cut(t.text, 22) + "\": FitBox " + m.FitBox + ", size " + t.fontSize.ToString("0.##") + " against " + want.ToString("0.##"));
                }
            }
            Assert.That(notRows, Is.EqualTo(3), "three rows have a note (set-up)");
            if (direct)
            {
                foreach (var name in new[] { "Fullscreen", "Graphics quality", "Text size", "Reduced motion", "Music" })
                {
                    var t = LabelWithText(view.Root, name);
                    Assert.That(t != null, "the label " + name + " exists (set-up)");
                    float want = Wanted(t.GetComponent<MythicTextScale>().BaseSize, scale);
                    if (Mathf.Abs(t.fontSize - want) > .05f) wrong.Add("\"" + name + "\" stands at " + t.fontSize.ToString("0.##") + ", wanted " + want.ToString("0.##"));
                }
            }
            Assert.That(wrong.Count, Is.EqualTo(0), wrong.Count + " labels of the card are not at their wanted size: " + string.Join("; ", wrong.GetRange(0, Mathf.Min(6, wrong.Count))));

            var differ = new List<string>();
            int atFloor = 0;
            for (int i = 0; i < fitted.Count; i++)
            {
                var m = fitted[i]; var t = m.GetComponent<TMP_Text>();
                if (before[i] <= m.MinSize + .01f && Wanted(m.BaseSize, scale) > m.MinSize + .01f) atFloor++;
                m.Refit();
                if (Mathf.Abs(before[i] - t.fontSize) > .05f) differ.Add("\"" + Cut(t.text, 24) + "\": " + before[i].ToString("0.##") + " against " + t.fontSize.ToString("0.##"));
            }
            Assert.That(differ.Count, Is.EqualTo(0), differ.Count + " of " + fitted.Count + " labels differ from a refit on their laid-out box (text: before against after): " + string.Join("; ", differ.GetRange(0, Mathf.Min(6, differ.Count))));
            Assert.That(atFloor, Is.EqualTo(0), atFloor + " labels stand at their floor size while a larger one is wanted");
        }

        /// <summary>The three note rows: height from their lines, the switch on the label line, the label above the note.</summary>
        internal static void AssertNoteRows(ModernSettingsView view, string at)
        {
            foreach (var name in NoteRows)
            {
                var label = LabelWithText(view.Root, name);
                Assert.That(label != null, name + " label exists (set-up) " + at);
                var row = (RectTransform)label.transform.parent;
                var note = row.Find("SettingNote").GetComponent<TMP_Text>();
                var value = (RectTransform)row.Find("Value");
                Assert.That(label.gameObject.activeInHierarchy && note.gameObject.activeInHierarchy, Is.True, name + ": label and note are awake (set-up) " + at);
                float l = label.preferredHeight, h = Mathf.Max(l, 20f);
                float n = note.GetPreferredValues(note.text, note.rectTransform.rect.width, 0f).y;
                float height = row.rect.height;
                Assert.That(height, Is.InRange(Mathf.Max(44f, h + n) - .01f, Mathf.Max(44f, h + n + 12f) + .01f),
                    name + " row is as tall as its lines: label line " + h + " + note " + n + " " + at);
                Assert.That(row.GetComponent<LayoutElement>().preferredHeight, Is.EqualTo(-1f), name + " row leaves its height to the layout group " + at);
                var labelBounds = BoundsIn(label.rectTransform, row); var noteBounds = BoundsIn(note.rectTransform, row);
                Assert.That(label.rectTransform.rect.height, Is.GreaterThanOrEqualTo(20f - .5f), name + " label line is at least the switch's height " + at);
                Assert.That(labelBounds.yMin, Is.GreaterThanOrEqualTo(noteBounds.yMax - .5f), name + " label lies above its note " + at);
                Assert.That(label.margin.z, Is.EqualTo(48f).Within(.01f), name + " label leaves the switch's place at its right " + at);
                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + .5f), name + " label text fits its box " + at);
                Assert.That(value.rect.width, Is.EqualTo(36f).Within(.01f), name + " switch width " + at);
                Assert.That(value.rect.height, Is.EqualTo(20f).Within(.01f), name + " switch height " + at);
                var valueBounds = BoundsIn(value, row);
                float gap = Mathf.Abs(valueBounds.center.y - labelBounds.center.y);
                Assert.That(gap, Is.LessThanOrEqualTo((h - 20f) / 2f + 1f), name + " switch centre is on the label's line (gap " + gap + ", label line " + h + ") " + at);
                float overlapX = Mathf.Min(valueBounds.xMax, noteBounds.xMax) - Mathf.Max(valueBounds.xMin, noteBounds.xMin);
                float overlapY = Mathf.Min(valueBounds.yMax, noteBounds.yMax) - Mathf.Max(valueBounds.yMin, noteBounds.yMin);
                Assert.That(Mathf.Max(0f, Mathf.Min(overlapX, overlapY)), Is.LessThanOrEqualTo(.5f), name + " switch does not cover the note " + at);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 1

        [Test]
        public void EveryLabelIsRightAtTheFirstOpenAfterAChangedEvent([Values(.85f, 1f, 1.25f, 1.5f)] float scale, [Values(false, true)] bool extraStep)
        {
            using (var rig = new SettingsRig(scale))
            {
                if (extraStep) rig.FireStep();
                Assert.That(rig.Changes, Is.EqualTo(extraStep ? 1 : 0), "the test fired exactly the Changed events it means to before the gear (set-up)");
                rig.Gear();
                Assert.That(rig.Settings.IsVisible, Is.True, "the card is open (set-up)");
                AssertCardRight(rig.Settings, scale, scale <= 1f);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 2

        [Test]
        public void TheCardAloneIsRightToo()
        {
            using (var env = new SettingsEnv(1f))
            {
                var canvasGo = new GameObject("settings labels alone canvas", typeof(RectTransform), typeof(Canvas));
                var sourceGo = new GameObject("settings labels alone source"); sourceGo.SetActive(false);
                var viewGo = new GameObject("settings labels alone owner");
                try
                {
                    canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1920, 1080);
                    var source = sourceGo.AddComponent<ShowcaseSettings>();
                    SettingsEnv.ScaleField.SetValue(source, 1f);
                    var view = viewGo.AddComponent<ModernSettingsView>();
                    view.Initialize(canvasGo.transform, source, null, new UiDisplayPreferences(), null);
                    view.SetVisible(true);
                    Assert.That(view.IsVisible, Is.True, "the card is open (set-up)");
                    AssertCardRight(view, 1f, true);
                }
                finally { UnityEngine.Object.DestroyImmediate(viewGo); UnityEngine.Object.DestroyImmediate(canvasGo); UnityEngine.Object.DestroyImmediate(sourceGo); }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 3

        [Test]
        public void AReopeningAndAResizeLeaveEveryLabelRight()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.FireStep();
                rig.Gear();
                AssertCardRight(rig.Settings, 1f, true);
                rig.Gear(); rig.Gear();
                Assert.That(rig.Settings.IsVisible, Is.True, "the card is open again (set-up)");
                AssertCardRight(rig.Settings, 1f, true);
                rig.Settings.ApplyViewport(new HudViewport(1f, 300f, 800f));
                Assert.That(rig.Settings.Root.sizeDelta.x, Is.EqualTo(276f).Within(.01f), "the narrow overlay's card is 276 wide (set-up)");
                AssertCardRight(rig.Settings, 1f, false);
                rig.Settings.ApplyViewport(SettingsRig.Wide);
                Assert.That(rig.Settings.Root.sizeDelta.x, Is.EqualTo(320f).Within(.01f), "the wide card is 320 wide (set-up)");
                AssertCardRight(rig.Settings, 1f, true);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 4

        [Test]
        public void ANoteWrapsAcrossTheRow([Values(.85f, 1f, 1.25f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.FireStep();
                rig.Gear();
                var note = LabelStartingWith(rig.Settings.Root, "Abilities you aim");
                Assert.That(note != null, "the Quick cast note exists (set-up)");
                Assert.That(note.gameObject.activeInHierarchy, Is.True, "the note is active in the hierarchy (set-up)");
                var fit = note.GetComponent<MythicTextScale>();
                Assert.That(fit.FitBox, Is.False, "the note is not fitted to its box");
                Assert.That(note.fontSize, Is.EqualTo(Wanted(UiTheme.Current.SizeSmall, scale)).Within(.01f), "the note stands at its wanted size");
                var row = (RectTransform)note.transform.parent;
                Assert.That(note.rectTransform.rect.width, Is.EqualTo(row.rect.width).Within(.5f), "the note's box is as wide as its row");
                float oneLine = note.GetPreferredValues("Ag").y;
                float need = note.GetPreferredValues(note.text, note.rectTransform.rect.width, 0f).y;
                Assert.That(need, Is.GreaterThan(1.5f * oneLine), "the note wraps over several lines at its width");
                Assert.That(note.rectTransform.rect.height, Is.GreaterThanOrEqualTo(need - .5f), "the note's box holds all its lines");
                var inRoot = BoundsIn(note.rectTransform, rig.Settings.Root); var inContent = BoundsIn(note.rectTransform, Content(rig.Settings));
                Assert.That(inRoot.xMin, Is.GreaterThanOrEqualTo(rig.Settings.Root.rect.xMin - .2f), "the note starts inside the card");
                Assert.That(inRoot.xMax, Is.LessThanOrEqualTo(rig.Settings.Root.rect.xMax + .2f), "the note ends inside the card");
                Assert.That(inContent.yMin, Is.GreaterThanOrEqualTo(Content(rig.Settings).rect.yMin - .2f), "the note ends inside the content");
                Assert.That(inContent.yMax, Is.LessThanOrEqualTo(Content(rig.Settings).rect.yMax + .2f), "the note starts inside the content");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 5

        [Test]
        public void EveryNoteRowGrowsByItsLines([Values(.85f, 1f, 1.25f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.FireStep();
                rig.Gear();
                AssertNoteRows(rig.Settings, "at " + scale);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 6

        [Test]
        public void TheLastRowIsStillReachable([Values(.85f, 1f, 1.25f, 1.5f)] float scale)
        {
            using (var rig = new SettingsRig(scale))
            {
                rig.Gear();
                SettingsRig.Update();
                var content = Content(rig.Settings); var scroll = Scroll(rig.Settings);
                Assert.That(rig.Settings.Root.sizeDelta.y, Is.EqualTo(740f).Within(.01f), "the card is at its cap, uiH - 60");
                Assert.That(content.rect.height, Is.GreaterThan(scroll.viewport.rect.height), "the content is taller than the viewport");
                var first = (RectTransform)content.GetChild(0); var last = (RectTransform)content.GetChild(content.childCount - 1);
                scroll.verticalNormalizedPosition = 0f; SettingsRig.Update(); LayoutRebuilder.ForceRebuildLayoutImmediate(content); SettingsRig.Update();
                var view = scroll.viewport.rect;
                var lastBounds = BoundsIn(last, scroll.viewport);
                Assert.That(lastBounds.yMin, Is.GreaterThanOrEqualTo(view.yMin - .2f), "at the bottom the last row's lower edge is in the viewport");
                Assert.That(lastBounds.yMax, Is.LessThanOrEqualTo(view.yMax + .2f), "at the bottom the last row's upper edge is in the viewport");
                scroll.verticalNormalizedPosition = 1f; SettingsRig.Update(); LayoutRebuilder.ForceRebuildLayoutImmediate(content); SettingsRig.Update();
                var firstBounds = BoundsIn(first, scroll.viewport);
                Assert.That(firstBounds.yMax, Is.LessThanOrEqualTo(view.yMax + .2f), "at the top the first row is in the viewport");
                Assert.That(firstBounds.yMin, Is.GreaterThanOrEqualTo(view.yMin - .2f), "at the top the first row's lower edge is in the viewport");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 7

        [Test]
        public void ASettledFitWritesNothingAndAllocatesNoMore()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.FireStep();
                rig.Gear();
                SettingsRig.Update();
                var settled = new List<TMP_Text>();
                foreach (var m in rig.Settings.Root.GetComponentsInChildren<MythicTextScale>(false))
                {
                    var t = m.GetComponent<TMP_Text>();
                    if (m.FitBox && Mathf.Abs(t.fontSize - Wanted(m.BaseSize, 1f)) < .01f && !t.havePropertiesChanged) settled.Add(t);
                }
                Assert.That(settled.Count, Is.GreaterThanOrEqualTo(20), "at least 20 labels stand at their wanted size and clean (set-up)");
                var content = Content(rig.Settings);
                var heights = new float[content.childCount];
                for (int i = 0; i < heights.Length; i++) heights[i] = ((RectTransform)content.GetChild(i)).rect.height;
                float cardHeight = rig.Settings.Root.sizeDelta.y;

                rig.Settings.ApplyViewport(SettingsRig.Wide);
                AssertSettled(rig, settled, heights, cardHeight, "the same viewport applied again");

                // A Changed event whose text scale is the one the card already fits does not fit again: only a text-size change alters a row.
                rig.Settings.Root.sizeDelta = new Vector2(rig.Settings.Root.sizeDelta.x, cardHeight + 33f);
                SettingsEnv.RaiseChanged(rig.Own);
                Assert.That(rig.Settings.Root.sizeDelta.y, Is.EqualTo(cardHeight + 33f).Within(.01f), "a Changed event at the same text scale does not fit the card (a fit would set its height back)");
                for (int i = 0; i < settled.Count; i++) Assert.That(settled[i].havePropertiesChanged, Is.False, "a Changed event at the same text scale writes no label: " + Cut(settled[i].text, 24));
                for (int i = 0; i < heights.Length; i++) Assert.That(((RectTransform)content.GetChild(i)).rect.height, Is.EqualTo(heights[i]).Within(.01f), "row " + i + " keeps its height");
                rig.Settings.Root.sizeDelta = new Vector2(rig.Settings.Root.sizeDelta.x, cardHeight);

                Action again = () => rig.Settings.ApplyViewport(SettingsRig.Wide);
                AllocationMeter.AssertAtMost(again, 20, FitBaseSamples, "a settled card's ApplyViewport");
            }
        }

        static void AssertSettled(SettingsRig rig, List<TMP_Text> settled, float[] heights, float cardHeight, string after)
        {
            for (int i = 0; i < settled.Count; i++) Assert.That(settled[i].havePropertiesChanged, Is.False, after + " writes no label: " + Cut(settled[i].text, 24));
            Assert.That(rig.Settings.Root.sizeDelta.y, Is.EqualTo(cardHeight).Within(.01f), after + ": the card keeps its height");
            var content = Content(rig.Settings);
            for (int i = 0; i < heights.Length; i++) Assert.That(((RectTransform)content.GetChild(i)).rect.height, Is.EqualTo(heights[i]).Within(.01f), after + ": row " + i + " keeps its height");
        }

        // ---------------------------------------------------------------------------------------------------------------- 8

        [Test]
        public void AHiddenFitRefitsNothingAndTheOpenDoes()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.FireStep();
                var label = LabelWithText(rig.Settings.Root, "Reduced motion");
                Assert.That(label != null, "the Reduced motion label exists (set-up)");
                Assert.That(label.GetComponent<MythicTextScale>().FitBox, Is.True, "a row without a note keeps its fitted label (set-up)");
                label.fontSize = 7.77f;
                rig.Settings.ApplyViewport(SettingsRig.Wide);
                Assert.That(rig.Settings.IsVisible, Is.False, "the card is hidden (set-up)");
                Assert.That(label.fontSize, Is.EqualTo(7.77f).Within(.001f), "a hidden fit refits nothing");
                rig.Gear();
                Assert.That(label.fontSize, Is.EqualTo(13f).Within(.05f), "the opening refits: 13, its wanted size");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 13

        [Test]
        public void TheOpenCardFollowsATextSizeChange()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.FireStep();
                rig.Settings.ApplyViewport(new HudViewport(1f, 1422f, 1200f));
                rig.Gear();
                SettingsRig.Update();
                float h1 = rig.Settings.Root.sizeDelta.y;
                Assert.That(h1, Is.LessThan(1140f - 1f), "the card is below its cap, so a change of height shows (set-up)");
                rig.Own.SetTextScale(1.5f);   // the real setting: no canvas update, no reopening
                Assert.That(rig.Own.TextScale, Is.EqualTo(1.5f).Within(.0001f), "the text size is 1.5 (set-up)");
                AssertCardRight(rig.Settings, 1.5f, false);
                AssertNoteRows(rig.Settings, "at 1.5 after a change while open");
                float h2 = rig.Settings.Root.sizeDelta.y;
                Assert.That(h2, Is.GreaterThan(h1), "the card grows with its text");
                Assert.That(h2, Is.EqualTo(36f + Content(rig.Settings).rect.height + 10f).Within(1f), "the card is its header, its content and 10");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 14

        [Test]
        public void TheCardShowsItsPositionCue()
        {
            using (var rig = new SettingsRig(1f))
            {
                rig.Gear();
                SettingsRig.Update();
                var scroll = Scroll(rig.Settings);
                Assert.That(Content(rig.Settings).rect.height, Is.GreaterThan(scroll.viewport.rect.height), "the content is taller than the viewport (set-up)");
                var track = rig.Settings.Root.Find("SettingsScroll/ScrollIndicator");
                Assert.That(track != null, "the card has a ScrollIndicator on its scroll frame");
                if (track == null) return;
                scroll.onValueChanged.Invoke(scroll.normalizedPosition);   // EditMode never delivers the ScrollRect's LateUpdate
                Assert.That(track.gameObject.activeSelf, Is.True, "a taller content shows the cue");
                rig.Settings.ApplyViewport(new HudViewport(1f, 1422f, 1600f));
                SettingsRig.Update();
                Assert.That(Content(rig.Settings).rect.height, Is.LessThanOrEqualTo(scroll.viewport.rect.height + .01f), "in a 1600 high window the content fits (set-up)");
                scroll.onValueChanged.Invoke(scroll.normalizedPosition);
                Assert.That(track.gameObject.activeSelf, Is.False, "a content that fits shows no cue");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- 12

        [Test]
        public void UnfitGivesTheWantedSizeAndNoFit()
        {
            using (var env = new SettingsEnv(1f))
            {
                var canvasGo = new GameObject("unfit canvas", typeof(RectTransform), typeof(Canvas));
                try
                {
                    canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(400, 400);
                    var box = HudUi.Box(canvasGo.transform, "box", HudAnchor.TopLeft, 0, 0, 20f, 14f);
                    var label = HudUi.Label(box, "label", "A long text that cannot fit its little box in one line", 13f, Color.white);
                    label.fontSize = 7.77f;
                    var fit = label.GetComponent<MythicTextScale>();
                    Assert.That(fit.FitBox, Is.True, "a HudUi.Label is fitted to its box (set-up)");
                    var same = HudUi.Unfit(label);
                    Assert.That(ReferenceEquals(same, label), Is.True, "Unfit returns the label it was given");
                    Assert.That(label.fontSize, Is.EqualTo(13f * Mathf.Clamp(env.Current.TextScale, .85f, 1.5f)).Within(.01f), "Unfit gives the wanted size");
                    Assert.That(fit.FitBox, Is.False, "and no box fit");
                    Assert.That(label.enableAutoSizing, Is.False, "and never auto-sizing");
                    fit.Refit();
                    Assert.That(label.fontSize, Is.EqualTo(13f).Within(.01f), "a refit keeps the wanted size");
                    MythicUi.ApplyTextScale(canvasGo.transform, 1.5f);
                    Assert.That(label.fontSize, Is.EqualTo(19.5f).Within(.01f), "the text-size setting still scales it");

                    var bare = new GameObject("bare", typeof(RectTransform)); bare.transform.SetParent(canvasGo.transform, false);
                    var plain = bare.AddComponent<TextMeshProUGUI>(); plain.fontSize = 17f;
                    Assert.That(ReferenceEquals(HudUi.Unfit(plain), plain), Is.True, "a bare TMP comes back");
                    Assert.That(plain.fontSize, Is.EqualTo(17f).Within(.001f), "and unchanged");
                }
                finally { UnityEngine.Object.DestroyImmediate(canvasGo); }
            }
        }
    }
}
