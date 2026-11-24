using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PLAYER-WORDS-2, the Field Ledger: the owner line names the status, the card tag lines and the detail read in words, and the longer
    /// tag line still fits its box at the narrowest cell. The rig is that of BuildDrawerEditModeTests (a drawer on a bare canvas).
    /// </summary>
    public sealed class LedgerWordsEditModeTests
    {
        GameObject _canvasObject, _host;
        BuildDrawer _drawer;

        [SetUp]
        public void SetUp()
        {
            _canvasObject = new GameObject("Ledger words canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _host = new GameObject("Ledger words host");
            _drawer = _host.AddComponent<BuildDrawer>();
            _drawer.Initialize(_canvasObject.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_canvasObject);
        }

        TMP_Text Detail => typeof(BuildDrawer).GetField("_detail", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_drawer) as TMP_Text;
        TMP_Text Tags(int card) => _drawer.Content.Find("Card" + card.ToString("00")).Find("CardTags").GetComponent<TMP_Text>();

        [Test]
        public void TheStatusLineNamesTheStatus()
        {
            _drawer.SetVisible(true);
            var me = new SelfView { Id = 17, Hp = 82, MaxHp = 120, Status = ActorStatus.Mia, Weapon = WeaponId.Axe, Kills = 4, Bounty = 2, Gv = 9 };
            _drawer.Refresh(new CollectionsView(), me, 1f);
            var status = _canvasObject.transform.Find("BuildDrawer/OwnerSummary/OwnerStatus").GetComponent<TMP_Text>();
            Assert.That(status.text, Does.StartWith("Missing in action  ·  Axe"));
            foreach (var (value, word) in new[] { (ActorStatus.Alive, "Alive"), (ActorStatus.Dead, "Dead"), (ActorStatus.Lobby, "Lobby") })
            {
                me.Status = value;
                _drawer.Refresh(new CollectionsView(), me, 1f);
                Assert.That(status.text, Does.StartWith(word + "  ·  Axe"), "status " + value);
            }
        }

        [Test]
        public void CardTagsReadAsWords()
        {
            Assert.That(Tags(65).text, Is.EqualTo("ON-HIT, DAMAGE OVER TIME, PHYSICAL"));
            Assert.That(Tags(56).text, Is.EqualTo("CRITICAL STRIKE, DAMAGE OVER TIME"));
            var code = new Regex(@"ONHIT|\bDOT\b|\bCRIT\b|_");
            for (int i = 0; i < CardDisplayCatalog.Count; i++)
                Assert.That(code.IsMatch(Tags(i).text), Is.False, "card " + i + " tag line '" + Tags(i).text + "' carries a code name");

            var buttons = _drawer.Content.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Length, Is.EqualTo(80), "set-up: eighty cards");
            buttons[65].onClick.Invoke();
            Assert.That(_drawer.SelectedIndex, Is.EqualTo(65), "set-up: card 65 is selected");
            StringAssert.Contains("On-hit, Damage over time, Physical", Detail.text);
            buttons[79].onClick.Invoke();
            Assert.That(_drawer.SelectedIndex, Is.EqualTo(79), "set-up: card 79 is selected");
            StringAssert.Contains("True, Execute, Returned", Detail.text);
            StringAssert.DoesNotContain("True_", Detail.text);
        }

        static Vector2 Needs(TMP_Text label, string text, float size, float width)
        {
            bool auto = label.enableAutoSizing; float keep = label.fontSize;
            label.enableAutoSizing = false; label.fontSize = size;
            var need = label.GetPreferredValues(text, width, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            return need;
        }

        [Test]
        public void GUARD_EveryCardTagLineFits()
        {
            // A standing guard on the tile at scale 1 in a 245 unit cell (the cell of a 1280 wide window): the tag line, measured at the smallest
            // size its auto-fit allows, needs no more height than its rect gives it, or no more than today's tag line needed (a card whose
            // words did not change, such as 79, may already overflow). Both geometry numbers come from the card's own rect anchors, so the
            // CreateCard remedy moves the guard: width = anchor share * 245 - 12, height = anchor share * 220 - 6 (the 12 and 6 are MythicUi.Text's
            // inset). Green on the stub; red once the words are longer, until the rect is raised. Why the narrowest cell (152) and scale 1.5
            // are not asserted: the packet's "Notes from the oracle author".
            _drawer.SetVisible(true);
            _drawer.Refresh(new CollectionsView(), null, 1f);
            var problems = new List<string>();
            for (int i = 0; i < CardDisplayCatalog.Count; i++)
            {
                var label = Tags(i); var rect = label.rectTransform;
                float width = (rect.anchorMax.x - rect.anchorMin.x) * 245f - 12f, height = (rect.anchorMax.y - rect.anchorMin.y) * 220f - 6f;
                string raw = CardDisplayCatalog.Get(i).Tags;
                string today = string.IsNullOrWhiteSpace(raw) ? "CARD" : raw.Replace("True_", "True").ToUpperInvariant();
                float allowed = Mathf.Max(height, Needs(label, today, label.fontSizeMin, width).y);
                var need = Needs(label, label.text, label.fontSizeMin, width);
                if (need.y > allowed + .01f) problems.Add("card " + i + " '" + label.text + "' needs " + need.y.ToString("0.#") + " of " + allowed.ToString("0.#") + " at min size " + label.fontSizeMin.ToString("0.#"));
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
