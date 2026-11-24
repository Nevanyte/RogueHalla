using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TEXT-AWAKE-1: a <c>FitBox</c> label built under an inactive parent has had no Awake and measures one tenth of its size, so
    /// <c>MythicTextScale</c> must not fit it then; it is fitted once, awake, when it wakes. In EditMode no callback runs by itself: a test
    /// activates the host and invokes <c>OnEnable</c> by reflection, which is what play does.
    /// </summary>
    public sealed class AsleepFitEditModeTests
    {
        const string LongText = "Old Wandering Villager from Far Hills";
        static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo OnEnableMethod = typeof(MythicTextScale).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);

        GameObject _canvas, _settingsGo, _threeD; ShowcaseSettings _settings; float _saved; bool _created; UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settingsGo = new GameObject("asleep fit settings"); _created = true;
                _settings = _settingsGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            }
            _saved = (float)ScaleField.GetValue(_settings);
            ScaleField.SetValue(_settings, 1f);
            _canvas = new GameObject("asleep fit canvas", typeof(RectTransform), typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(1920, 1080);
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) ScaleField.SetValue(_settings, _saved);
            if (_threeD != null) UnityEngine.Object.DestroyImmediate(_threeD);
            UnityEngine.Object.DestroyImmediate(_canvas);
            if (_created) UnityEngine.Object.DestroyImmediate(_settingsGo);
            UiTheme.Current = _savedTheme;
        }

        /// <summary>A HudUi.Box host, switched off BEFORE the label is made when asleep, then a HudUi.Label of size 14 in it.</summary>
        TMP_Text Make(string name, string text, float width, bool asleep, out GameObject host)
        {
            var box = HudUi.Box(_canvas.transform, "host-" + name, HudAnchor.Center, 0, 0, width, 18);
            host = box.gameObject;
            if (asleep) host.SetActive(false);
            return HudUi.Label(box, "label-" + name, text, 14, Color.white, TextAlignmentOptions.Center, false, UiFontRole.Display);
        }

        /// <summary>What play does when the host is activated: Unity calls OnEnable on the component.</summary>
        static void Wake(GameObject host, TMP_Text label)
        {
            host.SetActive(true);
            OnEnableMethod.Invoke(label.GetComponent<MythicTextScale>(), null);
        }

        [Test]
        public void AnAsleepLabelIsFittedWhenItWakes()
        {
            GameObject hostA, hostB;
            var twin = Make("twin", LongText, 120, false, out hostA);
            var asleep = Make("asleep", LongText, 120, true, out hostB);
            Assert.That(twin.fontSize, Is.LessThan(14f), "an awake label with this text in this box shrinks (set-up)");
            Assert.That(twin.isOrthographic, Is.True, "the twin is awake (set-up)");
            Assert.That(asleep.gameObject.activeInHierarchy, Is.False, "the label is built under an inactive parent (set-up)");
            Assert.That(asleep.isOrthographic, Is.False, "it has had no Awake (set-up)");
            Assert.That(asleep.fontSize, Is.EqualTo(14f).Within(.001f), "asleep, it keeps the wanted size");
            hostB.SetActive(true);
            Assert.That(asleep.isOrthographic, Is.True, "activating the host wakes it (set-up)");
            Assert.That(asleep.fontSize, Is.EqualTo(14f).Within(.001f), "no callback runs by itself in EditMode (set-up)");
            OnEnableMethod.Invoke(asleep.GetComponent<MythicTextScale>(), null);
            Assert.That(asleep.fontSize, Is.EqualTo(twin.fontSize).Within(.001f), "once awake it is fitted like its twin");
        }

        [Test]
        public void ATextSizeAppliedWhileAsleepIsFittedWhenItWakes()
        {
            GameObject hostA, hostB;
            var twin = Make("twin", LongText, 120, false, out hostA);
            var asleep = Make("asleep", LongText, 120, true, out hostB);
            MythicUi.ApplyTextScale(_canvas.transform, 1.5f);
            Assert.That(twin.fontSize, Is.LessThan(21f), "the awake twin shrinks at the larger size (set-up)");
            Assert.That(asleep.fontSize, Is.EqualTo(21f).Within(.001f), "asleep, it reads the wanted size, 14 x 1.5");
            Wake(hostB, asleep);
            Assert.That(asleep.fontSize, Is.EqualTo(twin.fontSize).Within(.001f), "once awake it is fitted at the stored scale, like its twin");
        }

        [Test]
        public void ATextThatFitsKeepsTheWantedSizeEitherWay()
        {
            GameObject hostA, hostB;
            var twin = Make("twin", "Soul Broker", 220, false, out hostA);
            var asleep = Make("asleep", "Soul Broker", 220, true, out hostB);
            Assert.That(twin.fontSize, Is.EqualTo(14f).Within(.001f), "a short text keeps the wanted size awake (set-up)");
            Assert.That(asleep.fontSize, Is.EqualTo(14f).Within(.001f), "and asleep");
            Wake(hostB, asleep);
            Assert.That(asleep.fontSize, Is.EqualTo(14f).Within(.001f), "and after it wakes");
        }

        [Test]
        public void ATextChangedWhileAsleepIsFittedWhenItWakes()
        {
            GameObject hostA, hostB;
            var twin = Make("twin", LongText, 120, false, out hostA);
            var asleep = Make("asleep", "Soul Broker", 120, true, out hostB);
            Assert.That(twin.fontSize, Is.LessThan(14f), "the awake twin with the long text shrinks (set-up)");
            asleep.text = LongText;
            Wake(hostB, asleep);
            Assert.That(asleep.fontSize, Is.EqualTo(twin.fontSize).Within(.001f), "the changed text is fitted when the label wakes");
        }

        [Test]
        public void AnAwakeLabelStillFitsAtOnce()
        {
            GameObject host;
            var label = Make("awake", LongText, 120, false, out host);
            var follow = label.GetComponent<MythicTextScale>();
            Assert.That(label.fontSize, Is.EqualTo(follow.MinSize).Within(.001f), "an awake label is fitted right after HudUi.Label, with no callback");
        }

        [Test]
        public void AThreeDimensionalLabelIsStillFittedAtOnce()
        {
            // Made as OverheadIndicators.Text does: a TextMeshPro that never sets isOrthographic in Awake.
            _threeD = new GameObject("three-d label");
            var text = _threeD.AddComponent<TextMeshPro>();
            var font = Resources.Load<TMP_FontAsset>("Showcase/NunitoBold");
            text.font = font != null ? font : TMP_Settings.defaultFontAsset;
            text.fontSize = 14; text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false;
            text.rectTransform.sizeDelta = new Vector2(2f, 4f);
            text.text = "A very long overhead name that is far wider than a box two units across, eighty chars";
            Assert.That(text.isOrthographic, Is.False, "a 3D TextMeshPro reads isOrthographic false even when active (set-up; if true, stop and report)");
            var follow = _threeD.AddComponent<MythicTextScale>(); follow.BaseSize = 14; follow.FitBox = true;
            follow.Apply(1f);
            Assert.That(text.fontSize, Is.EqualTo(follow.MinSize).Within(.001f), "the 3D label is fitted at once: the floor, not the wanted 14");
        }
    }
}
