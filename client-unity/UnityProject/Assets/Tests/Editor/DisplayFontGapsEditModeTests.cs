using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-HEADING-GAPS-1: headings in the display font (Cinzel Decorative) keep the gap between their words: the ink gap at every word boundary is 0.1 to 0.6 em, and the asset holds exactly the glyph pair records of the packet.</summary>
    public sealed class DisplayFontGapsEditModeTests
    {
        GameObject _canvas; UiTheme _saved;

        [SetUp]
        public void SetUp()
        {
            _saved = UiTheme.Current; UiTheme.Current = UiTheme.Placeholder();
            _canvas = new GameObject("heading gaps canvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) Object.DestroyImmediate(_canvas);
            UiTheme.Current = _saved;
        }

        // text, size passed to HudUi.Label, bold, characterSpacing, word boundaries
        static readonly object[] Strings =
        {
            new object[] { "TACTICAL MAP", 20f, false, 0f, 1 },
            new object[] { "LOBBY MAP", 20f, false, 0f, 1 },
            new object[] { "PLAN YOUR RUN", 20f, false, 0f, 2 },
            new object[] { "MAP KEY", 13f, false, 0f, 1 },
            new object[] { "IN VIEW", 13f, false, 0f, 1 },
            new object[] { "WEAPON COUNTERS", 13f, false, 0f, 1 },
            new object[] { "QUESTS & TRADERS", 13f, false, 0f, 2 },
            new object[] { "YOU DIED", 44f, true, 6f, 1 },
            new object[] { "START RUN", 17f, true, 0f, 1 },
            new object[] { "CONTROLS & GUIDE", 17f, true, 2f, 2 },
            new object[] { "ACCOUNT COSMETICS", 27f, false, 0f, 1 },
            new object[] { "THE FIELD GUIDE", 27f, false, 0f, 2 },
            new object[] { "QUEST JOURNAL", 27f, false, 0f, 1 },
            new object[] { "TRAINING CONSOLE", 20f, false, 0f, 1 },
            new object[] { "NEARBY QUEST ACTIONS", 18f, false, 0f, 2 },
            new object[] { "RESUME A SAVED RUN", 20f, true, 1.5f, 3 },
            new object[] { "SIGN-IN & ACCESS", 20f, true, 1.5f, 2 },
            new object[] { "RUN RESULTS", 20f, true, 1.5f, 1 },
            new object[] { "WEAPON COUNTERS", 18f, true, 0f, 1 },
            new object[] { "THE ARENA", 17f, true, 0f, 1 },
            new object[] { "Missing in action", 26f, true, 0f, 2 },
            new object[] { "No result", 26f, true, 0f, 1 },
            new object[] { "Chest 1 / 3", 30f, false, 2f, 3 },
        };

        // text, size, bold, gap in em of the single boundary (a first glyph without a foot: the table does not move it)
        static readonly object[] Untouched =
        {
            new object[] { "MAP KEY", 13f, false, .355f },
            new object[] { "LOBBY MAP", 20f, false, .142f },
            new object[] { "START RUN", 17f, true, .468f },
            new object[] { "THE ARENA", 17f, true, .385f },
        };

        // The 15 records of the packet: glyph index, first glyph character, xAdvance written.
        static readonly object[][] Table =
        {
            new object[] { 9, '&', 27.08f }, new object[] { 36, 'A', 20.46f }, new object[] { 42, 'G', 23.12f },
            new object[] { 46, 'K', 24.48f }, new object[] { 47, 'L', 41.4f }, new object[] { 48, 'M', 16.63f },
            new object[] { 49, 'N', 19.01f }, new object[] { 52, 'Q', 49.38f }, new object[] { 53, 'R', 24.91f },
            new object[] { 56, 'U', 12.32f }, new object[] { 57, 'V', 21.68f }, new object[] { 58, 'W', 21.68f },
            new object[] { 59, 'X', 14.12f }, new object[] { 61, 'Z', 29.52f }, new object[] { 84, 'q', 36.79f },
        };

        TMP_Text Build(Transform parent, string name, string text, float size, bool bold, float spacing, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var box = HudUi.At(parent, name + "Box", 0, 0, 1200, 80);
            var label = HudUi.Label(box, name, text, size, UiTheme.Current.Accent, align, bold, UiFontRole.Display);
            label.characterSpacing = spacing;
            label.ForceMeshUpdate();
            return label;
        }

        /// <summary>One ink gap, in em of the following glyph, per word boundary (a space with a non-space on each side).</summary>
        static float[] GapsInEm(TMP_FontAsset font, TMP_Text label)
        {
            label.ForceMeshUpdate();
            var ci = label.textInfo.characterInfo;
            var gaps = new List<float>();
            for (int i = 1; i < label.textInfo.characterCount - 1; i++)
            {
                if (ci[i].character != ' ' || ci[i - 1].character == ' ' || ci[i + 1].character == ' ') continue;
                var last = ci[i - 1]; var next = ci[i + 1];
                GlyphMetrics lm = last.textElement.glyph.metrics, nm = next.textElement.glyph.metrics;
                float inkEnd = last.origin + (lm.horizontalBearingX + lm.width) * last.scale;
                float inkStart = next.origin + nm.horizontalBearingX * next.scale;
                gaps.Add((inkStart - inkEnd) / (font.faceInfo.pointSize * next.scale));
            }
            return gaps.ToArray();
        }

        static float Wide(TMP_Text label) => label.GetPreferredValues(label.text, 0f, 0f).x;

        static string Join(float[] gaps) { var s = new List<string>(); foreach (var g in gaps) s.Add(g.ToString("0.000")); return string.Join(", ", s); }

        // ---- 1. every word boundary keeps a gap ----------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Strings))]
        public void EveryWordBoundaryKeepsAGapBetweenATenthAndSixTenthsOfAnEm(string text, float size, bool bold, float spacing, int boundaries)
        {
            var label = Build(_canvas.transform, "Text", text, size, bold, spacing);
            Assert.That(label.font.name, Is.EqualTo("CinzelDecorative"), "the Display role must resolve to the Cinzel asset, not the Nunito fallback");
            var gaps = GapsInEm(label.font, label);
            Assert.That(gaps.Length, Is.EqualTo(boundaries), text + ": the number of word boundaries");
            for (int i = 0; i < gaps.Length; i++)
                Assert.That(gaps[i], Is.InRange(0.1f, 0.6f), text + ": the ink gap at boundary " + i + " is " + gaps[i].ToString("0.000") + " em (all: " + Join(gaps) + ")");
        }

        // ---- 2. the pair table ---------------------------------------------------------------------------------------------------------

        [Test]
        public void ThePairTableHoldsExactlyTheSpecsRecords()
        {
            var font = UiTheme.Current.Font(UiFontRole.Display);
            Assert.That(font.name, Is.EqualTo("CinzelDecorative"));
            var records = font.fontFeatureTable.glyphPairAdjustmentRecords;
            Assert.That(records.Count, Is.EqualTo(15), "the asset holds exactly 15 pair records");
            foreach (var row in Table)
            {
                uint index = (uint)(int)row[0]; float want = (float)row[2];
                GlyphPairAdjustmentRecord found = default; int matches = 0;
                foreach (var r in records) if (r.firstAdjustmentRecord.glyphIndex == index) { found = r; matches++; }
                Assert.That(matches, Is.EqualTo(1), "one record for glyph " + index + " (" + row[1] + ")");
                if (matches == 0) continue;
                var v = found.firstAdjustmentRecord.glyphValueRecord;
                Assert.That(v.xAdvance, Is.EqualTo(want).Within(.01f), "xAdvance of " + row[1]);
                Assert.That(v.xPlacement, Is.EqualTo(0f), "xPlacement of " + row[1]);
                Assert.That(v.yPlacement, Is.EqualTo(0f), "yPlacement of " + row[1]);
                Assert.That(v.yAdvance, Is.EqualTo(0f), "yAdvance of " + row[1]);
                var second = found.secondAdjustmentRecord;
                Assert.That(second.glyphIndex, Is.EqualTo(3u), "the second glyph of " + row[1] + " is the space (index 3)");
                Assert.That(second.glyphValueRecord.xPlacement + second.glyphValueRecord.yPlacement + second.glyphValueRecord.xAdvance + second.glyphValueRecord.yAdvance, Is.EqualTo(0f), "the space's value record is all zero");
                Assert.That((int)found.featureLookupFlags, Is.EqualTo(0), "feature lookup flags of " + row[1]);
            }
            // The rule, so a re-baked asset with other metrics fails: overhang over 0.06 em gets overhang + 0.05 em.
            float pt = font.faceInfo.pointSize;
            foreach (var pair in font.characterTable)
            {
                if (pair.unicode == ' ') continue;
                var m = pair.glyph.metrics;
                float overhang = m.horizontalBearingX + m.width - m.horizontalAdvance;
                if (overhang <= .06f * pt) continue;
                GlyphPairAdjustmentRecord rec = default; bool has = false;
                foreach (var r in records) if (r.firstAdjustmentRecord.glyphIndex == pair.glyphIndex) { rec = r; has = true; }
                Assert.That(has, Is.True, "glyph '" + (char)pair.unicode + "' overhangs by " + overhang.ToString("0.00") + " units and needs a record");
                if (has) Assert.That(rec.firstAdjustmentRecord.glyphValueRecord.xAdvance, Is.EqualTo(overhang + .05f * pt).Within(.01f), "xAdvance of '" + (char)pair.unicode + "' is its overhang plus 0.05 em");
            }
        }

        // ---- 3. a glyph without a foot keeps its gap -----------------------------------------------------------------------------------

        [TestCaseSource(nameof(Untouched))]
        public void AGlyphWithoutAFootKeepsItsGap(string text, float size, bool bold, float expectedEm)
        {
            var label = Build(_canvas.transform, "Text", text, size, bold, 0f);
            var gaps = GapsInEm(label.font, label);
            Assert.That(gaps.Length, Is.EqualTo(1), text + ": one boundary");
            if (gaps.Length == 1) Assert.That(gaps[0], Is.EqualTo(expectedEm).Within(.01f), text + ": the gap is " + gaps[0].ToString("0.000") + " em");
        }

        // ---- 4. the planning heading and the widest timer -------------------------------------------------------------------------------

        [Test]
        public void ThePlanningHeadingLeavesRoomForTheWidestTimer()
        {
            float w = MapScreenLayout.Compute(640f, 480f, MapScreenMode.Planning).Title.W;
            Assert.That(w, Is.EqualTo(228f).Within(.01f), "the planning title box at 640 x 480");
            var box = HudUi.At(_canvas.transform, "PlanTitle", 0, 0, w, 40f);
            var heading = HudUi.Label(box, "Heading", "PLAN YOUR RUN", 20f, UiTheme.Current.Accent, TextAlignmentOptions.MidlineLeft, false, UiFontRole.Display);
            var timer = HudUi.Label(box, "Timer", "40s", 26f, UiTheme.Current.Accent, TextAlignmentOptions.MidlineRight, false, UiFontRole.Display);
            heading.ForceMeshUpdate(); timer.ForceMeshUpdate();
            Assert.That(heading.font.name, Is.EqualTo("CinzelDecorative"), "heading face");
            Assert.That(timer.font.name, Is.EqualTo("CinzelDecorative"), "timer face");
            Assert.That(heading.fontSize, Is.EqualTo(20f), "heading size (text scale 1)");
            Assert.That(timer.fontSize, Is.EqualTo(26f), "timer size (text scale 1)");
            Assert.That(Wide(heading), Is.EqualTo(178.8f).Within(1f), "the kern reaches the heading: PLAN YOUR RUN is 178.8 wide");
            Assert.That(Wide(heading) + Wide(timer), Is.LessThanOrEqualTo(w), "heading " + Wide(heading) + " plus timer " + Wide(timer) + " in " + w);
        }
    }
}
