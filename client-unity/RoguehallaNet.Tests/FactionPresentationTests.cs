using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    public sealed class FactionPresentationTests
    {
        [Test]
        public void NamesAndRolesMatchTheStormInkChampionOrderWithoutChangingFactionCodes()
        {
            string[] names = { "Fire", "Haunted", "Plague", "Frost", "Holy" };
            string[] roles = { "Burst duelist · woman", "Reaper bruiser · man", "Hooded infection necromancer", "Burly guardian · man", "Armored paladin · woman" };
            for (int faction = 0; faction < FactionPresentation.Count; faction++)
            {
                Assert.AreEqual(names[faction], FactionPresentation.Name(faction));
                Assert.AreEqual(roles[faction], FactionPresentation.Role(faction));
                Assert.AreEqual("Gilded " + names[faction], FactionPresentation.GildedName(faction));
                Assert.AreEqual(names[faction], MapZones.Name(faction + 1));
            }
            Assert.AreEqual(1, (byte)Roguehalla.Net.FactionId.Sentenced);
            Assert.AreEqual("Unknown", FactionPresentation.Name(-1));
            Assert.AreEqual("Unknown role", FactionPresentation.Role(5));
        }
    }
}
