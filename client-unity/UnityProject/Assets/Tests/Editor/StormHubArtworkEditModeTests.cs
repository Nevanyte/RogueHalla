using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    public sealed class StormHubArtworkEditModeTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        GameObject CanvasRoot()
        {
            _root = new GameObject("Storm hub UI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            return _root;
        }

        [Test]
        public void WalkableFloorUsesGuildHallSpriteAndKeepsItsPointerSurface()
        {
            var root = CanvasRoot();
            var expected = Resources.Load<Sprite>(HubArtworkContract.GuildHallResource);
            Assert.IsNotNull(expected, "Root art must provide the GuildHall single Sprite at the agreed Resources key.");

            var view = root.AddComponent<HubAtriumView>();
            view.Initialize(root.transform, _ => { });
            var floor = view.Room.Find("Floor");
            var image = floor.GetComponent<Image>();
            Assert.AreSame(expected, image.sprite);
            Assert.IsTrue(image.raycastTarget, "The floor Image is the existing walk-to-point hit surface.");
            Assert.IsNotNull(floor.GetComponent("HubFloorPointer"));
            Assert.IsFalse(floor.Find("FloorTint").GetComponent<Image>().raycastTarget);

            var shop = floor.parent.Find("Station_Shop").GetComponent<Button>();
            Assert.IsTrue(shop.targetGraphic.raycastTarget);
            Assert.That(shop.colors.normalColor.a, Is.LessThanOrEqualTo(.70f));
            Assert.That(shop.colors.disabledColor.a, Is.LessThanOrEqualTo(.48f));
            Assert.IsTrue(shop.GetComponentInChildren<TMPro.TMP_Text>(true).text.Contains("Shop"));
        }

        [Test]
        public void GuideFactionsUsePortraitCellsWithoutChangingChoiceTextOrRaycasts()
        {
            var root = CanvasRoot();
            var guide = root.AddComponent<HubGuideView>();
            guide.Initialize((RectTransform)root.transform, _ => { });
            guide.ShowTab(HubGuideView.GuideTab.Champions);

            var factions = new[] { "Fire", "Sentenced", "Plague", "Frost", "Holy" };
            for (int i = 0; i < factions.Length; i++)
            {
                var cardIcon = guide.Content.Find("Card_" + i + "/Head/FactionIcon").GetComponent<Image>();
                var choice = guide.Content.Find("FactionChoices/Faction_" + factions[i]);
                var choiceIcon = choice.Find("FactionIcon").GetComponent<Image>();
                var title = guide.Content.Find("Card_" + i + "/Head/Title").GetComponent<TMPro.TMP_Text>();
                Assert.AreSame(HubArtworkAssets.FactionPortrait(i), cardIcon.sprite, factions[i] + " card portrait");
                Assert.AreSame(HubArtworkAssets.FactionPortrait(i), choiceIcon.sprite, factions[i] + " choice portrait");
                Assert.IsFalse(cardIcon.raycastTarget);
                Assert.IsFalse(choiceIcon.raycastTarget);
                StringAssert.Contains(factions[i], title.text);
                Assert.IsNotNull(choice.GetComponent<Button>());
            }
            Assert.That(guide.FocusRoot.GetComponent<Image>().color.a, Is.LessThan(1f));
        }

        [Test]
        public void ShopCardsLoadTheMatchingPreviewForEveryCatalogEquipmentEntry()
        {
            var root = CanvasRoot();
            var view = root.AddComponent<HubCosmeticsView>();
            view.Initialize(root.transform, null);
            view.Show(HubCosmeticsTab.Shop);
            var rebuild = typeof(HubCosmeticsView).GetMethod("RebuildCards", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(rebuild);
            rebuild.Invoke(view, null);

            var ids = new[]
            {
                "gilded-fire", "gilded-sentenced", "gilded-plague", "gilded-frost", "gilded-holy",
                "gilded-sword", "gilded-axe", "gilded-bow", "gilded-wand", "gilded-shield"
            };
            foreach (string id in ids)
            {
                var card = view.Root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Cosmetic_" + id);
                var preview = card.Find("EquipmentPreview").GetComponent<Image>();
                Assert.IsNotNull(preview.sprite, id + " preview sprite");
                Assert.AreSame(HubArtworkAssets.EquipmentPreview(id), preview.sprite, id + " catalog mapping");
                Assert.IsFalse(preview.raycastTarget, "The image is decorative; catalog label and purchase control remain operable.");
                Assert.IsNotNull(card.Find("ItemName").GetComponent<TMPro.TMP_Text>());
            }
            Assert.That(view.Root.GetComponent<Image>().color.a, Is.LessThan(1f));
        }
    }
}
