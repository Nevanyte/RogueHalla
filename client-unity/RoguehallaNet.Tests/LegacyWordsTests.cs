using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-PLAYER-WORDS-3: every table word that legacy also words on the same surface equals legacy's, letter for letter.</summary>
    public class LegacyWordsTests
    {
        sealed class Row
        {
            public readonly string Id;
            public readonly Func<string> Unity;
            public readonly string Legacy;
            public readonly string Where;

            public Row(string id, Func<string> unity, string legacy, string where)
            {
                Id = id;
                Unity = unity;
                Legacy = legacy;
                Where = where;
            }
        }

        const string Abilities = "packages/shared/src/abilities.ts";
        const string Chest = "packages/shared/src/chestBoosts.ts";
        const string Traders = "packages/shared/src/traders.ts";
        const string Glossary = "packages/shared/src/glossary.ts";
        const string Hud = "packages/client/src/ui/hud.ts";
        const string Gained = "packages/client/src/ui/buildHud.ts";

        static Row Wish(AbilityId id, string legacy, int line) => new Row("AbilityId." + id, () => PlayerWords.Word(id), legacy, Abilities + ":" + line);
        static Row Title(BoostId id, string legacy, int line) => new Row("ChestTitle." + id, () => PlayerWords.ChestTitle(id), legacy, Chest + ":" + line);
        static Row Stat(TradeStat id, string legacy) => new Row("TradeStat." + id, () => PlayerWords.Word(id), legacy, Traders + ":13-14");
        static Row Tag(string code, string legacy, int line) => new Row("Tag." + code, () => PlayerWords.CardTag(code), legacy, Glossary + ":" + line);
        static Row Cell(int i, string name, string legacy) => new Row("Gained." + name, () => BuildHudText.Cells(new WireBuildSummary())[i].Label, legacy, Gained + ":154-157");

        static readonly Row[] Rows =
        {
            Wish(AbilityId.Freeze, "Freeze", 175), Wish(AbilityId.Swarm, "Swarm", 185), Wish(AbilityId.Trial, "Trial", 199),
            Wish(AbilityId.Blaze, "Blaze Step", 212), Wish(AbilityId.LastStand, "Last Stand", 226),
            new Row("PropAppearance.WindEmblem", () => PlayerWords.Word(PropAppearance.WindEmblem), "Wind emblem", "packages/server/src/sim/consumables.ts:178"),
            Title(BoostId.Ad, "Offense", 24), Title(BoostId.Ap, "Soul Offence", 25), Title(BoostId.Armor, "Defense", 26), Title(BoostId.Shield, "Soul Defense", 27),
            Title(BoostId.MaxHp, "Max HP", 28), Title(BoostId.AttackSpeed, "Attack Speed", 29), Title(BoostId.MoveSpeed, "Move Speed", 30), Title(BoostId.Lifesteal, "Leech", 31),
            Stat(TradeStat.MaxHp, "max HP"), Stat(TradeStat.Ad, "Offense"), Stat(TradeStat.Ap, "Soul Offence"), Stat(TradeStat.AttackSpeed, "attack speed"),
            Stat(TradeStat.MoveSpeed, "movement speed"), Stat(TradeStat.Armor, "Defense"), Stat(TradeStat.Shield, "Soul Defense"),
            Tag("Physical", "Physical", 20), Tag("Magic", "Magic", 24), Tag("True_", "True", 28), Tag("Dot", "Damage over time", 32), Tag("OnHit", "On-hit", 36),
            Tag("Returned", "Returned", 40), Tag("Slow", "Slow", 44), Tag("Heal", "Heal", 48), Tag("Block", "Block", 52), Tag("Ramping", "Ramping", 56),
            Tag("Execute", "Execute", 60), Tag("Crit", "Critical strike", 64), Tag("Area", "Area", 68), Tag("Wish", "Wish", 72),
            Cell(0, "Offense", "Offense"), Cell(2, "Damage", "Damage gained"), Cell(3, "AttackSpeed", "Attack speed"), Cell(4, "LifeSteal", "Life steal"),
            Cell(5, "Health", "Health gained"), Cell(8, "MoveSpeed", "Move speed"),
            new Row("SessionState.Ended", () => PlayerWords.Word(SessionState.Ended), "Run over", Hud + ":54"),
        };

        // Every other enum value: a KEPT, NONE, NEARBY or DOUBT row (see the packet's table), not pinned here.
        static readonly string[] Others =
        {
            "ActorStatus.Lobby", "ActorStatus.Alive", "ActorStatus.Dead", "ActorStatus.Mia",
            "PropAppearance.Ground", "PropAppearance.Chest", "PropAppearance.Body", "PropAppearance.Corpse", "PropAppearance.Cannon",
            "PropAppearance.QuestItem", "PropAppearance.Heal", "PropAppearance.RebirthEgg",
            "TradeKind.Barter", "TradeKind.Stats",
            "SessionState.Idle", "SessionState.Connecting", "SessionState.HelloSent", "SessionState.InMatch", "SessionState.Reconnecting",
            "SessionState.Rejected", "SessionState.Failed", "SessionState.Detached",
            "ActorKind.Player", "ActorKind.RegionalMob", "ActorKind.Zombie", "ActorKind.TrainingDummy", "ActorKind.QuestGiver",
            "BuildTip.Ad", "BuildTip.Ap", "BuildTip.Armor", "BuildTip.Shield", "BuildTip.MaxHp", "BuildTip.AttackSpeed", "BuildTip.MoveSpeed", "BuildTip.Lifesteal",
        };

        static readonly string[] AllTags = { "True_", "Dot", "OnHit", "Crit", "Physical", "Magic", "Returned", "Slow", "Heal", "Block", "Ramping", "Execute", "Area", "Wish" };

        [TestCase(BoostId.MaxHp, "Max HP")]
        [TestCase(BoostId.AttackSpeed, "Attack Speed")]
        [TestCase(BoostId.MoveSpeed, "Move Speed")]
        [TestCase(BoostId.Lifesteal, "Leech")]
        public void ADifferentChestTitleIsLegacys(BoostId id, string legacy)
        {
            Assert.AreEqual(legacy, PlayerWords.ChestTitle(id), "chest card title for " + id + " (packages/shared/src/chestBoosts.ts:24-31)");
        }

        static void Cover<T>(HashSet<string> known, List<string> missing, string prefix) where T : struct, Enum
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                string id = (prefix ?? typeof(T).Name) + "." + value;
                if (!known.Contains(id)) missing.Add(id);
            }
        }

        [Test]
        public void EveryTableWordWithALegacyWordIsLegacys()
        {
            var rowIds = new HashSet<string>(Rows.Select(r => r.Id));
            var all = new HashSet<string>(rowIds);
            foreach (var other in Others) all.Add(other);
            Assert.AreEqual(Rows.Length + Others.Length, rowIds.Count + Others.Length, "an id appears twice in Rows");
            Assert.AreEqual(42, Rows.Length, "the pinned walk holds 42 rows");

            var missing = new List<string>();
            Cover<AbilityId>(all, missing, null);
            Cover<ActorStatus>(all, missing, null);
            Cover<PropAppearance>(all, missing, null);
            Cover<TradeKind>(all, missing, null);
            Cover<TradeStat>(all, missing, null);
            Cover<SessionState>(all, missing, null);
            Cover<ActorKind>(all, missing, null);
            Cover<BoostId>(rowIds, missing, "ChestTitle");
            Cover<BoostId>(new HashSet<string>(Others), missing, "BuildTip");
            foreach (var tag in AllTags) if (!rowIds.Contains("Tag." + tag)) missing.Add("Tag." + tag);
            Assert.IsEmpty(missing, "enum values without a verdict (add each to Rows or Others): " + string.Join(", ", missing));

            var mismatches = new List<string>();
            foreach (var row in Rows)
            {
                string unity = row.Unity();
                if (unity != row.Legacy) mismatches.Add(row.Id + ": Unity '" + unity + "', legacy '" + row.Legacy + "' (" + row.Where + ")");
            }
            Assert.IsEmpty(mismatches, mismatches.Count + " word(s) differ from legacy:\n" + string.Join("\n", mismatches));
        }
    }
}
