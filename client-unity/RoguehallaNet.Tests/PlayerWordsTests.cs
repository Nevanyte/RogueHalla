using System;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-PLAYER-WORDS-1: the player-facing words for an actor kind and the format of an IN VIEW row.</summary>
    public class PlayerWordsTests
    {
        static readonly Regex Camel = new Regex("[a-z][A-Z]");

        static string Row(string faction, ActorKind kind, int count)
        {
            var text = new StringBuilder();
            PlayerWords.AppendContactRow(text, faction, kind, count);
            return text.ToString();
        }

        [Test]
        public void EveryActorKindHasAPlayerWord()
        {
            var kinds = new System.Collections.Generic.List<ActorKind>((ActorKind[])Enum.GetValues(typeof(ActorKind)));
            var defined = kinds.Count;
            kinds.Add((ActorKind)5);
            kinds.Add((ActorKind)255);
            for (int i = 0; i < kinds.Count; i++)
            {
                var kind = kinds[i];
                bool isDefined = i < defined;
                Assert.That(PlayerWords.IsNamed(kind), Is.EqualTo(isDefined), "IsNamed " + (int)kind);
                foreach (var word in new[] { PlayerWords.Singular(kind), PlayerWords.Plural(kind) })
                {
                    string what = "kind " + (int)kind + " word '" + word + "'";
                    Assert.That(word, Is.Not.Null.And.Not.Empty, what);
                    Assert.That(word, Is.EqualTo(word.Trim()), what + " has stray whitespace");
                    Assert.That(word, Does.Not.Contain("_"), what);
                    Assert.That(Camel.IsMatch(word), Is.False, what + " looks like an identifier");
                    if (isDefined && Camel.IsMatch(kind.ToString()))
                        Assert.That(word.Equals(kind.ToString(), StringComparison.OrdinalIgnoreCase), Is.False, what + " is the identifier");
                }
            }
        }

        [TestCase(ActorKind.Player, "player", "players")]
        [TestCase(ActorKind.RegionalMob, "creature", "creatures")]
        [TestCase(ActorKind.Zombie, "zombie", "zombies")]
        [TestCase(ActorKind.TrainingDummy, "training dummy", "training dummies")]
        [TestCase(ActorKind.QuestGiver, "quest giver", "quest givers")]
        public void TheWordsAreTheOnesTheOwnerReads(ActorKind kind, string singular, string plural)
        {
            Assert.That(PlayerWords.Singular(kind), Is.EqualTo(singular));
            Assert.That(PlayerWords.Plural(kind), Is.EqualTo(plural));
        }

        [TestCase(5)]
        [TestCase(255)]
        public void AnUnknownKindGetsANeutralWord(int code)
        {
            Assert.That(PlayerWords.Singular((ActorKind)code), Is.EqualTo("contact"));
            Assert.That(PlayerWords.Plural((ActorKind)code), Is.EqualTo("contacts"));
        }

        [Test]
        public void ARowReadsFactionWordAndCount()
        {
            Assert.That(Row("Haunted", ActorKind.RegionalMob, 12), Is.EqualTo("Haunted · creatures x12"));
            Assert.That(Row("Plague", ActorKind.RegionalMob, 1), Is.EqualTo("Plague · creature x1"));
            Assert.That(Row("Plague", ActorKind.TrainingDummy, 1), Is.EqualTo("Plague · training dummy x1"));
            Assert.That(Row("Plague", ActorKind.TrainingDummy, 2), Is.EqualTo("Plague · training dummies x2"));
            Assert.That(Row("Haunted", ActorKind.Zombie, 3), Is.EqualTo("Haunted · zombies x3"));
            Assert.That(Row("Fire", (ActorKind)9, 4), Is.EqualTo("Fire · contacts x4"));
            Assert.That(Row("Fire", (ActorKind)9, 1), Is.EqualTo("Fire · contact x1"));
            Assert.That(Row(null, ActorKind.QuestGiver, 2), Is.EqualTo("Quest givers x2"));
            Assert.That(Row("", ActorKind.QuestGiver, 1), Is.EqualTo("Quest giver x1"));
            Assert.That(Row(null, ActorKind.RegionalMob, 1), Is.EqualTo("Creature x1"));

            var text = new StringBuilder("A\n");
            PlayerWords.AppendContactRow(text, "Haunted", ActorKind.RegionalMob, 12);
            Assert.That(text.ToString(), Is.EqualTo("A\nHaunted · creatures x12"));

            foreach (var row in new[] { Row("Haunted", ActorKind.RegionalMob, 12), Row(null, ActorKind.RegionalMob, 3), Row("Plague", ActorKind.QuestGiver, 1) })
            {
                Assert.That(row, Does.Not.Contain("RegionalMob"));
                Assert.That(row, Does.Not.EndWith("\n"));
                Assert.That(row, Is.Not.Empty);
            }
        }
    }
}
