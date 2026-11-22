using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;

namespace Roguehalla.Net.Tests
{
    public sealed class StormHubArtworkContractTests
    {
        static string PresentationCatalog()
        {
            using (var stream = typeof(StormHubArtworkContractTests).Assembly.GetManifestResourceStream("Account.PresentationCosmetics.json"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        [Test]
        public void PortraitAndEquipmentAtlasCellsFollowTheLiveAccountCatalogOrder()
        {
            Assert.IsTrue(AccountFullJson.TryCosmeticCatalog(PresentationCatalog(), out var catalog));
            var equipment = catalog.Where(item => item.Slot.StartsWith("gold-", StringComparison.Ordinal)).ToArray();
            var expected = new[]
            {
                "gilded-fire", "gilded-sentenced", "gilded-plague", "gilded-frost", "gilded-holy",
                "gilded-sword", "gilded-axe", "gilded-bow", "gilded-wand", "gilded-shield"
            };
            CollectionAssert.AreEqual(expected, equipment.Select(item => item.Id).ToArray());
            Assert.AreEqual(5, HubArtworkContract.EquipmentColumns);
            Assert.AreEqual(2, HubArtworkContract.EquipmentRows);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(i, HubArtworkContract.EquipmentPreviewCell(equipment[i].Id), expected[i]);

            var factions = new[] { "Fire", "Sentenced", "Plague", "Frost", "Holy" };
            for (int i = 0; i < factions.Length; i++)
                Assert.AreEqual(i, HubArtworkContract.FactionPortraitCell(factions[i]), factions[i]);
            Assert.AreEqual(-1, HubArtworkContract.EquipmentPreviewCell("title-gladiator"));
            Assert.AreEqual(-1, HubArtworkContract.FactionPortraitCell("unknown"));
        }

        [Test]
        public void ArtworkResourceKeysMatchTheUnityResourcesContract()
        {
            Assert.AreEqual("StormInk/Hub/GuildHall", HubArtworkContract.GuildHallResource);
            Assert.AreEqual("StormInk/Hub/FactionPortraits", HubArtworkContract.FactionPortraitsResource);
            Assert.AreEqual("StormInk/Hub/EquipmentPreviews", HubArtworkContract.EquipmentPreviewsResource);
            Assert.AreEqual(256, HubArtworkContract.CellPixels);
        }
    }
}
