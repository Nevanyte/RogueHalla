using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using MapRect = Roguehalla.Net.Ui.MapRect;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-TABMAP-TEXT-2: the Tab map key learns each camp buff kind's amount from the delivered rows (CampAmountKey), strips the texts the key
    /// says, packs the kinds seen into cells, and CampKeyLayout holds the numbers of the key and the rail below it. The packet
    /// (docs/backlog/packets/P-UI-TABMAP-TEXT-2.md) has the rules.
    /// </summary>
    public sealed class CampAmountKeyTests
    {
        // the quanta of the shipped table, by kind
        static readonly (byte stat, ushort amount)[] Quanta =
        {
            (0, 10000), (1, 10000), (2, 100), (3, 100), (4, 25), (5, 100), (6, 12500), (7, 15000), (8, 50),
        };

        static readonly string[] WordsOfKinds =
            { "Offense", "Soul Offence", "Damage", "Attack speed", "Life steal", "Max health", "Defense", "Soul Defense", "Move speed" };

        static CampGlyphRow Row(int kind, ushort amount, bool elite = false)
        {
            var reward = new WireCampReward { Stat = (byte)kind, Amount = amount };
            return new CampGlyphRow
            {
                IconKey = CampBadges.IconKey((byte)kind), IconRgb = CampBadges.IconRgb((byte)kind), AmountText = CampBadges.AmountText(reward),
                Elite = elite, EliteText = elite ? "Aug" : null,
            };
        }

        static CampGlyphRow Damage3() => Row(2, 100);

        static List<CampGlyphRow> NineKinds()
        {
            var rows = new List<CampGlyphRow>();
            for (int s = 0; s < 9; s++) rows.Add(Row(s, Quanta[s].amount));
            return rows;
        }

        [Test]
        public void TheNineKindsAreTheKeysOfCampBadges()
        {
            Assert.That(CampAmountKey.Kinds, Is.EqualTo(9), "nine kinds");
            Assert.That(CampAmountKey.IconKeys.Length, Is.EqualTo(9), "nine icon keys");
            Assert.That(CampAmountKey.Words, Is.EqualTo(WordsOfKinds), "the nine words");
            for (int s = 0; s < 9 && s < CampAmountKey.IconKeys.Length; s++)
            {
                Assert.That(CampAmountKey.IconKeys[s], Is.EqualTo(CampBadges.IconKey((byte)s)), "icon key " + s);
                Assert.That(CampAmountKey.KindOf(CampAmountKey.IconKeys[s]), Is.EqualTo(s), "kind of icon key " + s);
            }
            Assert.That(CampAmountKey.KindOf(null), Is.EqualTo(-1));
            Assert.That(CampAmountKey.KindOf("chest"), Is.EqualTo(-1));
        }

        [Test]
        public void AFixedKindIsStrippedWhenTheKeyShowsItAndKeptWhenNot()
        {
            var key = new CampAmountKey();
            var rows = new List<CampGlyphRow> { Damage3(), Damage3(), Row(4, 25) };
            key.Apply(rows, false);
            Assert.That(key.Version, Is.EqualTo(2), "two kinds learned");
            Assert.That(rows[0].AmountText, Is.EqualTo("+3%"), "strip off: kept 0");
            Assert.That(rows[1].AmountText, Is.EqualTo("+3%"), "strip off: kept 1");
            Assert.That(rows[2].AmountText, Is.EqualTo("+0.75%"), "strip off: kept 2");
            key.Apply(rows, true);
            Assert.That(key.Version, Is.EqualTo(2), "nothing new to learn");
            Assert.That(rows[0].AmountText, Is.Null, "strip on: 0");
            Assert.That(rows[1].AmountText, Is.Null, "strip on: 1");
            Assert.That(rows[2].AmountText, Is.Null, "strip on: 2");
        }

        [Test]
        public void TheCellsAreTheShippedTablesAmounts()
        {
            var key = new CampAmountKey();
            key.Apply(NineKinds(), true);
            Assert.That(key.Version, Is.EqualTo(9), "nine kinds learned");
            string[] texts = { "+3 Offense", "+3 Soul Offence", "+3% Damage", "+3% Attack speed", "+0.75% Life steal", "+3% Max health", "+3.75 Defense", "+4.5 Soul Defense", "+1.5% Move speed" };
            for (int s = 0; s < 9; s++)
            {
                Assert.That(key.Has(s), Is.True, "has " + s);
                Assert.That(key.CellText(s), Is.EqualTo(texts[s]), "cell text " + s);
                var cell = key.Cell(s);
                Assert.That(cell.Kind, Is.EqualTo(s), "cell kind " + s);
                Assert.That(cell.IconKey, Is.EqualTo(CampBadges.IconKey((byte)s)), "cell icon key " + s);
                Assert.That(cell.IconRgb, Is.EqualTo(CampBadges.IconRgb((byte)s)), "cell colour " + s);
                Assert.That(cell.Text, Is.EqualTo(texts[s]), "cell text field " + s);
            }
        }

        [Test]
        public void ADifferingAmountMakesTheKindVaryAndKeepsEveryText()
        {
            var key = new CampAmountKey();
            var rows = new List<CampGlyphRow> { Row(2, 100), Row(2, 200) };
            Assert.That(rows[0].AmountText, Is.EqualTo("+3%"), "premise: the first amount");
            Assert.That(rows[1].AmountText, Is.EqualTo("+6%"), "premise: the second amount");
            key.Apply(rows, true);
            Assert.That(key.Version, Is.EqualTo(2), "one for the first, one for the change");
            Assert.That(rows[0].AmountText, Is.EqualTo("+3%"), "text 0 kept");
            Assert.That(rows[1].AmountText, Is.EqualTo("+6%"), "text 1 kept");
            Assert.That(key.IsFixed(2), Is.False, "not fixed");
            Assert.That(key.CellText(2), Is.EqualTo("Damage · varies"));
        }

        [Test]
        public void TheLatchMovesOnlyOneWay()
        {
            var key = new CampAmountKey();
            key.Apply(new List<CampGlyphRow> { Row(2, 100), Row(2, 200) }, true);
            Assert.That(key.Has(2), Is.True, "premise: damage is seen");
            Assert.That(key.Version, Is.EqualTo(2), "premise: version 2");
            var rows = new List<CampGlyphRow> { Damage3() };
            key.Apply(rows, true);
            Assert.That(key.IsFixed(2), Is.False, "still not fixed");
            Assert.That(rows[0].AmountText, Is.EqualTo("+3%"), "text kept");
            Assert.That(key.Version, Is.EqualTo(2), "no change");
        }

        [Test]
        public void AnUnknownKeyAndAMissingAmountAreLeftAlone()
        {
            var key = new CampAmountKey();
            var rows = new List<CampGlyphRow>
            {
                Damage3(),
                new CampGlyphRow { IconKey = "chest", AmountText = "+9" },
                new CampGlyphRow { IconKey = null, AmountText = "+3" },
            };
            key.Apply(rows, true);
            Assert.That(rows[0].AmountText, Is.Null, "premise: the damage row is stripped");
            Assert.That(key.Has(2), Is.True, "premise: damage is seen");
            Assert.That(key.Version, Is.EqualTo(1), "premise: one kind learned");
            Assert.That(rows[1].AmountText, Is.EqualTo("+9"), "the chest row keeps its text");
            Assert.That(rows[2].AmountText, Is.EqualTo("+3"), "the null-key row keeps its text");

            var fresh = new CampAmountKey();
            var second = new List<CampGlyphRow> { new CampGlyphRow { IconKey = CampBadges.IconKey(2), AmountText = null }, Damage3() };
            fresh.Apply(second, true);
            Assert.That(fresh.Version, Is.EqualTo(1), "the null amount was not learned");
            Assert.That(fresh.IsFixed(2), Is.True, "fixed");
            Assert.That(fresh.CellText(2), Is.EqualTo("+3% Damage"));
            Assert.That(second[0].AmountText, Is.Null, "the null row stays null");
        }

        [Test]
        public void TheEliteTagLeavesWithTheKeyAndTheFlagStays()
        {
            var key = new CampAmountKey();
            var rows = new List<CampGlyphRow> { Row(2, 100, true) };
            key.Apply(rows, true);
            Assert.That(rows[0].EliteText, Is.Null, "strip on: no tag");
            Assert.That(rows[0].Elite, Is.True, "the flag stays");

            var off = new CampAmountKey();
            var kept = new List<CampGlyphRow> { Row(2, 100, true) };
            off.Apply(kept, false);
            Assert.That(kept[0].EliteText, Is.EqualTo("Aug"), "strip off: the tag stays");

            var varies = new CampAmountKey();
            var mixed = new List<CampGlyphRow> { Row(2, 100), Row(2, 200, true) };
            varies.Apply(mixed, true);
            Assert.That(mixed[1].AmountText, Is.EqualTo("+6%"), "an elite row of a not-fixed kind keeps its amount");
            Assert.That(mixed[1].EliteText, Is.Null, "its tag still leaves");
        }

        [Test]
        public void ForgetStartsOver()
        {
            var key = new CampAmountKey();
            key.Apply(NineKinds(), true);
            Assert.That(key.Version, Is.EqualTo(9), "premise: nine kinds");
            key.Forget();
            for (int s = 0; s < 9; s++) Assert.That(key.Has(s), Is.False, "forgotten " + s);
            Assert.That(key.Count, Is.EqualTo(0), "no kinds");
            Assert.That(key.Version, Is.GreaterThan(9), "the version moved on");
            var rows = new List<CampGlyphRow> { Row(2, 200) };
            key.Apply(rows, true);
            Assert.That(key.IsFixed(2), Is.True, "the new amount is the first, so fixed");
            Assert.That(key.CellText(2), Is.EqualTo("+6% Damage"));
        }

        [Test]
        public void VersionMovesOnlyOnAChange()
        {
            var key = new CampAmountKey();
            var rows = new List<CampGlyphRow> { Damage3(), Damage3() };
            key.Apply(rows, false);
            Assert.That(key.Version, Is.EqualTo(1), "one kind learned");
            for (int i = 0; i < 10; i++) key.Apply(rows, false);
            Assert.That(key.Version, Is.EqualTo(1), "ten identical applies");
        }

        [Test]
        public void ApplyAllocatesNothing()
        {
            var template = new CampGlyphRow[50];
            for (int k = 0; k < 50; k++) template[k] = Row(k % 9, Quanta[k % 9].amount, k % 4 == 0);
            var rows = new List<CampGlyphRow>(50);
            for (int k = 0; k < 50; k++) rows.Add(template[k]);
            var key = new CampAmountKey();
            key.Apply(rows, false);
            Assert.That(key.Version, Is.EqualTo(9), "nine kinds learned");
            double bytes = AllocationProbe.PerCall(i =>
            {
                for (int k = 0; k < 50; k++) rows[k] = template[k];
                key.Apply(rows, (i & 1) == 0);
            }, 2000);
            Assert.That(bytes, Is.EqualTo(0.0), "Apply allocates nothing");
        }

        [Test]
        public void TheLayoutRule()
        {
            Assert.That(CampKeyLayout.Rows, Is.EqualTo(17));
            Assert.That(CampKeyLayout.RowH, Is.EqualTo(19f));
            Assert.That(CampKeyLayout.KeyTop, Is.EqualTo(34f));
            Assert.That(CampKeyLayout.KeyH, Is.EqualTo(332f));
            Assert.That(CampKeyLayout.ViewY, Is.EqualTo(366f));
            Assert.That(CampKeyLayout.ViewHead, Is.EqualTo(22f));
            Assert.That(CampKeyLayout.ListFoot, Is.EqualTo(112f));
            Assert.That(CampKeyLayout.MinList, Is.EqualTo(110f));
            Assert.That(CampKeyLayout.MinWidth, Is.EqualTo(240f));
            Assert.That(CampKeyLayout.MinHeight, Is.EqualTo(610f));
            Assert.That(CampKeyLayout.ListHeight(674f), Is.EqualTo(174f));
            Assert.That(CampKeyLayout.ListHeight(615f), Is.EqualTo(115f));
            Assert.That(CampKeyLayout.ListHeight(610f), Is.EqualTo(110f));
            AssertCell(CampKeyLayout.Cell(0, 250f), 0f, 76f, 125f, 19f, "slot 0 at 250");
            AssertCell(CampKeyLayout.Cell(1, 250f), 125f, 76f, 125f, 19f, "slot 1 at 250");
            AssertCell(CampKeyLayout.Cell(8, 290f), 0f, 152f, 145f, 19f, "slot 8 at 290");
            AssertCell(CampKeyLayout.Cell(9, 250f), 0f, 0f, 0f, 0f, "slot 9");
            AssertCell(CampKeyLayout.Cell(-1, 250f), 0f, 0f, 0f, 0f, "slot -1");
            Assert.That(CampKeyLayout.InKey(true, 250f, 674f), Is.True, "tab, roomy");
            Assert.That(CampKeyLayout.InKey(true, 250f, 610f), Is.True, "tab, exactly the minimum height");
            Assert.That(CampKeyLayout.InKey(true, 250f, 609f), Is.False, "one unit short");
            Assert.That(CampKeyLayout.InKey(true, 239f, 674f), Is.False, "too narrow");
            Assert.That(CampKeyLayout.InKey(false, 250f, 674f), Is.False, "planning");
        }

        static void AssertCell(MapRect r, float x, float y, float w, float h, string what)
        {
            Assert.That(new[] { r.X, r.Y, r.W, r.H }, Is.EqualTo(new[] { x, y, w, h }), what);
        }

        [Test]
        public void TheSeenKindsPackFromSlotZeroInKindOrder()
        {
            var key = new CampAmountKey();
            key.Apply(new List<CampGlyphRow> { Row(6, 12500), Row(0, 10000), Row(2, 100) }, true);
            Assert.That(key.Count, Is.EqualTo(3), "three kinds seen");
            Assert.That(key.SlotOf(0), Is.EqualTo(0));
            Assert.That(key.SlotOf(2), Is.EqualTo(1));
            Assert.That(key.SlotOf(6), Is.EqualTo(2));
            Assert.That(key.SlotOf(1), Is.EqualTo(-1));
            Assert.That(key.KindAt(0), Is.EqualTo(0));
            Assert.That(key.KindAt(1), Is.EqualTo(2));
            Assert.That(key.KindAt(2), Is.EqualTo(6));
            Assert.That(key.KindAt(3), Is.EqualTo(-1));
            key.Apply(new List<CampGlyphRow> { Row(6, 12500), Row(0, 10000), Row(2, 100), Row(1, 10000) }, true);
            Assert.That(key.SlotOf(2), Is.EqualTo(2), "damage moved by one");
            Assert.That(key.SlotOf(6), Is.EqualTo(3), "defense moved by one");
            Assert.That(key.Count, Is.EqualTo(4));
        }
    }
}
