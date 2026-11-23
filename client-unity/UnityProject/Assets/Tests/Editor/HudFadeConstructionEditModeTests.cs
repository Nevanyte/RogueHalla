using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-MOTION-1-fix oracle: a HudFade's CanvasGroup reflects the state the fade starts in, from construction.</summary>
    public sealed class HudFadeConstructionEditModeTests
    {
        const float Dt = 1f / 60f;
        GameObject _parent;
        RectTransform _rect;
        bool? _savedOverride;

        [SetUp]
        public void SetUp()
        {
            _savedOverride = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            _parent = new GameObject("fade construction parent", typeof(RectTransform));
            ((RectTransform)_parent.transform).sizeDelta = new Vector2(800f, 600f);
            var go = new GameObject("fade construction child", typeof(RectTransform));
            _rect = (RectTransform)go.transform;
            _rect.SetParent(_parent.transform, false);
            _rect.sizeDelta = new Vector2(100f, 60f);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = _savedOverride;
            if (_parent != null) Object.DestroyImmediate(_parent);
        }

        CanvasGroup Group()
        {
            var g = _rect.GetComponent<CanvasGroup>();
            Assert.That(g, Is.Not.Null, "HudFade leaves a CanvasGroup on its root");
            return g;
        }

        [Test]
        public void ANonInteractiveFadeOnAnActiveRootTakesNoPointerFromConstruction()
        {
            Assert.That(_rect.gameObject.activeSelf, Is.True);
            Assert.That(_rect.GetComponent<CanvasGroup>(), Is.Null);
            new HudFade(_rect, false);
            Assert.That(Group().blocksRaycasts, Is.False, "blocksRaycasts right after construction");
            Assert.That(Group().interactable, Is.False, "interactable right after construction");
        }

        [Test]
        public void ANonInteractiveFadeOnARootWithItsOwnGroupTakesNoPointerFromConstruction()
        {
            var own = _rect.gameObject.AddComponent<CanvasGroup>();
            own.blocksRaycasts = true; own.interactable = true;
            new HudFade(_rect, false);
            Assert.That(own.blocksRaycasts, Is.False, "blocksRaycasts right after construction");
            Assert.That(own.interactable, Is.False, "interactable right after construction");
        }

        [Test]
        public void SettingTheWantedStateAgainLeavesItNonBlocking()
        {
            var fade = new HudFade(_rect, false);
            fade.Set(true);
            fade.Tick(Dt);
            Assert.That(Group().blocksRaycasts, Is.False, "blocksRaycasts after Set(true) and a tick");
            Assert.That(Group().interactable, Is.False, "interactable after Set(true) and a tick");
        }

        [Test]
        public void AnInteractiveFadeOnAnActiveRootKeepsThePointer()
        {
            // Green on the base: guards the fix against taking the pointer from an interactive panel.
            new HudFade(_rect, true);
            Assert.That(Group().blocksRaycasts, Is.True);
            Assert.That(Group().interactable, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AFadeBuiltOnAnInactiveRootBlocksNothing(bool interactive)
        {
            _rect.gameObject.SetActive(false);
            new HudFade(_rect, interactive);
            Assert.That(Group().blocksRaycasts, Is.False, "blocksRaycasts, interactive " + interactive);
            Assert.That(Group().interactable, Is.False, "interactable, interactive " + interactive);
        }
    }
}
