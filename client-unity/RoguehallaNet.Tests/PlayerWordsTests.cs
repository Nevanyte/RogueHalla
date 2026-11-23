using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
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

        // ---- P-UI-PLAYER-WORDS-2: the tables of the other code values and the card tags

        static readonly HashSet<string> LeaveAsIs = new HashSet<string> { "Freeze", "Swarm", "Trial", "Chest", "Body", "Corpse", "Lifesteal", "Lobby", "Alive", "Dead" };
        static readonly HashSet<string> TagsAsWritten = new HashSet<string> { "Physical", "Magic", "Returned", "Slow", "Heal", "Block", "Ramping", "Execute", "Area", "Wish" };
        static readonly string[] AllTags = { "True_", "Dot", "OnHit", "Crit", "Physical", "Magic", "Returned", "Slow", "Heal", "Block", "Ramping", "Execute", "Area", "Wish" };

        static void WordRules(string word, string what)
        {
            Assert.That(word, Is.Not.Null.And.Not.Empty, what);
            Assert.That(word, Is.EqualTo(word.Trim()), what + " has stray whitespace");
            Assert.That(word, Does.Not.Contain("_"), what);
            Assert.That(Camel.IsMatch(word), Is.False, what + " looks like an identifier");
        }

        static void Sweep<T>(Func<T, bool> named, Func<T, string> word, T undefined, string neutral) where T : struct, Enum
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                string id = value.ToString(), what = typeof(T).Name + "." + id + " word '" + word(value) + "'";
                Assert.That(named(value), Is.True, "IsNamed " + typeof(T).Name + "." + id);
                WordRules(word(value), what);
                if (!LeaveAsIs.Contains(id)) Assert.That(word(value).Equals(id, StringComparison.OrdinalIgnoreCase), Is.False, what + " is the identifier");
            }
            string un = typeof(T).Name + " undefined word '" + word(undefined) + "'";
            Assert.That(named(undefined), Is.False, "IsNamed undefined " + typeof(T).Name);
            WordRules(word(undefined), un);
            Assert.That(word(undefined), Is.EqualTo(neutral), un);
        }

        [Test]
        public void EveryAddedEnumHasAPlayerWord()
        {
            Sweep<AbilityId>(PlayerWords.IsNamed, PlayerWords.Word, (AbilityId)200, "Wish");
            Sweep<ActorStatus>(PlayerWords.IsNamed, PlayerWords.Word, (ActorStatus)200, "Unknown");
            Sweep<PropAppearance>(PlayerWords.IsNamed, PlayerWords.Word, (PropAppearance)200, "Item");
            Sweep<BoostId>(PlayerWords.IsNamed, PlayerWords.Word, (BoostId)200, "Bonus");
            Sweep<TradeKind>(PlayerWords.IsNamed, PlayerWords.Word, (TradeKind)200, "Trader");
            Sweep<TradeStat>(PlayerWords.IsNamed, PlayerWords.Word, (TradeStat)200, "stat");
            Sweep<SessionState>(PlayerWords.IsNamed, PlayerWords.Word, (SessionState)99, "Unknown");
            foreach (var tag in AllTags)
            {
                string what = "tag " + tag + " word '" + PlayerWords.CardTag(tag) + "'";
                Assert.That(PlayerWords.IsNamedCardTag(tag), Is.True, "IsNamedCardTag " + tag);
                WordRules(PlayerWords.CardTag(tag), what);
                if (!TagsAsWritten.Contains(tag)) Assert.That(PlayerWords.CardTag(tag).Equals(tag, StringComparison.OrdinalIgnoreCase), Is.False, what + " is the identifier");
            }
            Assert.That(PlayerWords.IsNamedCardTag("Bogus"), Is.False, "IsNamedCardTag Bogus");
            WordRules(PlayerWords.CardTag("Bogus"), "unnamed tag");
            Assert.That(PlayerWords.CardTag("Bogus"), Is.EqualTo("Other"));
        }

        [Test]
        public void TheWordsAreTheOnesTheGameReads()
        {
            void Check<T>(Func<T, string> word, params (T, string)[] table) { foreach (var (v, w) in table) Assert.That(word(v), Is.EqualTo(w), typeof(T).Name + "." + v); }
            Check<AbilityId>(PlayerWords.Word, (AbilityId.Freeze, "Freeze"), (AbilityId.Swarm, "Swarm"), (AbilityId.Trial, "Trial"), (AbilityId.Blaze, "Blaze Step"), (AbilityId.LastStand, "Last Stand"), ((AbilityId)200, "Wish"));
            Check<ActorStatus>(PlayerWords.Word, (ActorStatus.Lobby, "Lobby"), (ActorStatus.Alive, "Alive"), (ActorStatus.Dead, "Dead"), (ActorStatus.Mia, "Missing in action"), ((ActorStatus)200, "Unknown"));
            Check<PropAppearance>(PlayerWords.Word, (PropAppearance.Ground, "Weapon"), (PropAppearance.Chest, "Chest"), (PropAppearance.Body, "Body"), (PropAppearance.Corpse, "Corpse"),
                (PropAppearance.WindEmblem, "Wind emblem"), (PropAppearance.Cannon, "Travel cannon"), (PropAppearance.QuestItem, "Quest item"), (PropAppearance.Heal, "Heal pickup"),
                (PropAppearance.RebirthEgg, "Rebirth Egg"), ((PropAppearance)200, "Item"));
            Check<BoostId>(PlayerWords.Word, (BoostId.Ad, "Offense"), (BoostId.Ap, "Soul Offence"), (BoostId.Armor, "Defense"), (BoostId.Shield, "Soul Defense"), (BoostId.MaxHp, "Max health"),
                (BoostId.AttackSpeed, "Attack speed"), (BoostId.MoveSpeed, "Move speed"), (BoostId.Lifesteal, "Lifesteal"), ((BoostId)200, "Bonus"));
            Check<TradeKind>(PlayerWords.Word, (TradeKind.Barter, "Weapon barter"), (TradeKind.Stats, "Stat trader"), ((TradeKind)200, "Trader"));
            Check<TradeStat>(PlayerWords.Word, (TradeStat.MaxHp, "max HP"), (TradeStat.Ad, "Offense"), (TradeStat.Ap, "Soul Offence"), (TradeStat.AttackSpeed, "attack speed"),
                (TradeStat.MoveSpeed, "movement speed"), (TradeStat.Armor, "Defense"), (TradeStat.Shield, "Soul Defense"), ((TradeStat)200, "stat"));
            Check<SessionState>(PlayerWords.Word, (SessionState.Idle, "Not connected"), (SessionState.Connecting, "Connecting\u2026"), (SessionState.HelloSent, "Connecting\u2026"),
                (SessionState.InMatch, "In the match"), (SessionState.Reconnecting, "Reconnecting\u2026"), (SessionState.Ended, "The match has ended."), (SessionState.Rejected, "Connection rejected"),
                (SessionState.Failed, "Connection failed"), (SessionState.Detached, "Disconnected"), ((SessionState)99, "Unknown"));
            var expectedTags = new Dictionary<string, string>
            {
                ["True_"] = "True", ["Dot"] = "Damage over time", ["OnHit"] = "On-hit", ["Crit"] = "Critical strike", ["Physical"] = "Physical", ["Magic"] = "Magic", ["Returned"] = "Returned",
                ["Slow"] = "Slow", ["Heal"] = "Heal", ["Block"] = "Block", ["Ramping"] = "Ramping", ["Execute"] = "Execute", ["Area"] = "Area", ["Wish"] = "Wish", ["Bogus"] = "Other",
            };
            foreach (var pair in expectedTags) Assert.That(PlayerWords.CardTag(pair.Key), Is.EqualTo(pair.Value), "tag " + pair.Key);
        }

        static WireHudContent Block(params (HudTerm code, double value)[] t)
        {
            return new WireHudContent { Terms = t.OrderBy(r => (byte)r.code).Select(r => new WireHudTerm { Code = (byte)r.code, Value = r.value }).ToArray() };
        }

        [Test]
        public void TheWishNameIsTheChampionRowName()
        {
            var cases = new[]
            {
                (FactionId.Fire, AbilityId.Blaze, "Blaze Step"), (FactionId.Sentenced, AbilityId.Trial, "Trial"), (FactionId.Plague, AbilityId.Swarm, "Swarm"),
                (FactionId.Frost, AbilityId.Freeze, "Freeze"), (FactionId.Holy, AbilityId.LastStand, "Last Stand"),
            };
            foreach (var (faction, ability, word) in cases)
            {
                Assert.That(HudContentText.WishName(null, ability), Is.EqualTo(word), "WishName(null, " + ability + ")");
                Assert.That(HudContentText.Wish(null, faction).Name, Is.EqualTo(HudContentText.WishName(null, ability)), faction + " row name");
            }
            var wall = Block((HudTerm.FrostWish, 1));
            Assert.That(wall.FrostWish, Is.EqualTo(FrostWishKind.Wall), "set-up: the block says wall");
            Assert.That(HudContentText.WishName(wall, AbilityId.Freeze), Is.EqualTo("Frost Wall"));
            foreach (var (faction, ability, word) in cases.Where(c => c.Item2 != AbilityId.Freeze))
                Assert.That(HudContentText.WishName(wall, ability), Is.EqualTo(word), "under the wall: " + ability);
        }

        [Test]
        public void CardTagsReadAsWords()
        {
            Assert.That(PlayerWords.CardTags("OnHit, Dot, Physical"), Is.EqualTo("On-hit, Damage over time, Physical"));
            Assert.That(PlayerWords.CardTags("True_, Execute, Returned"), Is.EqualTo("True, Execute, Returned"));
            Assert.That(PlayerWords.CardTags(" Magic ,Dot"), Is.EqualTo("Magic, Damage over time"));
            Assert.That(PlayerWords.CardTags("Bogus"), Is.EqualTo("Other"));
            Assert.That(PlayerWords.CardTags(null), Is.EqualTo(""));
            Assert.That(PlayerWords.CardTags(""), Is.EqualTo(""));
            Assert.That(PlayerWords.CardTags(" "), Is.EqualTo(""));
            for (int i = 0; i < CardDisplayCatalog.Count; i++)
            {
                string raw = CardDisplayCatalog.Get(i).Tags, words = PlayerWords.CardTags(raw);
                string what = "card " + i + " '" + raw + "' reads '" + words + "'";
                Assert.That(words, Does.Not.Contain("_"), what);
                Assert.That(Camel.IsMatch(words), Is.False, what);
                Assert.That(words, Does.Not.Contain("Other"), what);
                Assert.That(words.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length), what + ": one word per tag");
                Assert.That(ChestOptionText.Describe(new RewardOptionView { Card = (CardId)i }).Tags, Is.EqualTo(words), "chest card " + i);
            }
        }
    }
}
