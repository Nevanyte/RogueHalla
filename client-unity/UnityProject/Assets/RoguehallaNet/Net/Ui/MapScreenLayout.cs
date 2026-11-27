using System;

namespace Roguehalla.Net.Ui
{
    /// <summary>A rectangle in HUD units from the top-left corner of the stage (x right, y down).</summary>
    public readonly struct MapRect
    {
        public readonly float X, Y, W, H;
        public MapRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right { get { return X + W; } }
        public float Bottom { get { return Y + H; } }
        public bool Overlaps(MapRect o) { return X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom; }
        public bool Within(float width, float height) { return X >= -.01f && Y >= -.01f && Right <= width + .01f && Bottom <= height + .01f; }
    }

    public enum MapScreenMode { Tab, Planning }

    /// <summary>
    /// Layout of the full-screen map (the held Tab map and the planning screen), modelled on legacy <c>planning.css</c> /
    /// <c>mapRoster.css</c>: the map is a square, flanked by two columns (Tab: roster/key on the left, weapon guide and quests on the
    /// right; planning: a quest board on the left and a 340 wide decision column on the right). The block is centred, so there is no letterbox beside the square.
    /// Below 900 units wide the columns stack under the map. Pure; every number is in HUD units.
    /// </summary>
    public readonly struct MapScreenLayout
    {
        public const float SheetPad = 12f;
        public const float Pad = 12f, Gap = 12f, FramePad = 4f, TitleHeight = 34f, TabLegendHeight = 30f, PlanningTitleHeight = 40f, PlanningNoteHeight = 52f;
        public const float StackBelow = 900f, ChromeClear = 44f, PlanningColumn = 340f, PlanningQuestColumn = 260f;
        public const float MapMin = 240f, MapMax = 820f;

        public readonly MapScreenMode Mode;
        public readonly bool Stacked;
        /// <summary>The heading strip above the map frame.</summary>
        public readonly MapRect Title;
        /// <summary>The backing panel behind the whole Tab block on the wide layout (stacked and planning: equals <see cref="Body"/>, not drawn).</summary>
        public readonly MapRect Sheet;
        /// <summary>The full-width heading strip of the sheet (stacked and planning: equals <see cref="Title"/>).</summary>
        public readonly MapRect Header;
        /// <summary>The panel that frames the map (the map square plus <see cref="FramePad"/> on every side).</summary>
        public readonly MapRect Frame;
        /// <summary>The square the map is drawn in.</summary>
        public readonly MapRect Map;
        /// <summary>The strip under the map: the zone legend (Tab) or the route summary (planning).</summary>
        public readonly MapRect Legend;
        /// <summary>Tab: roster/key column (the whole block starts below the top-right chrome, 44 units down). Planning: the left public quest board.</summary>
        public readonly MapRect Left;
        /// <summary>Tab: weapon guide and quest column. Planning: the decision column.</summary>
        public readonly MapRect Right;
        /// <summary>The bounding box of everything above.</summary>
        public readonly MapRect Body;

        MapScreenLayout(MapScreenMode mode, bool stacked, MapRect title, MapRect frame, MapRect legend, MapRect left, MapRect right, MapRect body, MapRect? sheet = null, MapRect? header = null)
        {
            Mode = mode; Stacked = stacked; Title = title; Frame = frame; Legend = legend; Left = left; Right = right; Body = body;
            Sheet = sheet ?? body; Header = header ?? title;
            Map = new MapRect(frame.X + FramePad, frame.Y + FramePad, frame.W - FramePad * 2f, frame.H - FramePad * 2f);
        }

        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        /// <summary><paramref name="textScale"/> widens the side columns (the player's "Map text size", 1 to 1.5).</summary>
        public static MapScreenLayout Compute(float uiWidth, float uiHeight, MapScreenMode mode, float textScale = 1f)
        {
            if (float.IsNaN(uiWidth) || uiWidth < 1f) uiWidth = 1f;
            if (float.IsNaN(uiHeight) || uiHeight < 1f) uiHeight = 1f;
            if (float.IsNaN(textScale) || textScale < 1f) textScale = 1f;
            textScale = Math.Min(textScale, 1.5f);
            return mode == MapScreenMode.Tab ? Tab(uiWidth, uiHeight, textScale) : Planning(uiWidth, uiHeight);
        }

        static MapScreenLayout Tab(float w, float h, float ts)
        {
            float col = Clamp((float)Math.Round(250f * ts), 190f, 290f);
            float legend = TabLegendHeight;
            if (w < StackBelow)
            {
                float avail = w - Pad * 2f;
                float side = (float)Math.Floor(Math.Min(avail - FramePad * 2f, .64f * h));
                side = Clamp(side, 120f, MapMax);
                float fw = side + FramePad * 2f;
                float x0 = (float)Math.Floor((w - Math.Max(fw, avail)) / 2f);
                float y = ChromeClear;
                var title = new MapRect(x0 + (Math.Max(fw, avail) - fw) / 2f, y, fw, TitleHeight);
                var frame = new MapRect(title.X, title.Bottom + Gap / 2f, fw, fw);
                var leg = new MapRect(title.X, frame.Bottom + Gap / 2f, fw, legend);
                float colW = (float)Math.Floor((avail - Gap) / 2f);
                float top = leg.Bottom + Gap; float rest = Math.Max(60f, h - Pad - top);
                var left = new MapRect(Pad, top, colW, rest);
                var right = new MapRect(Pad + colW + Gap, top, colW, rest);
                return new MapScreenLayout(MapScreenMode.Tab, true, title, frame, leg, left, right, new MapRect(Pad, ChromeClear, avail, h - ChromeClear - Pad));
            }
            float fixedH = SheetPad * 2f + TitleHeight + Gap + Gap + legend + FramePad * 2f;
            float sideMax = (float)Math.Floor(Math.Min(h - ChromeClear - Pad - fixedH, w - Pad * 2f - SheetPad * 2f - col * 2f - Gap * 2f - FramePad * 2f));
            float mapSide = Clamp(sideMax, MapMin, MapMax);
            float frameW = mapSide + FramePad * 2f;
            float innerW = col * 2f + Gap * 2f + frameW;
            float innerH = TitleHeight + Gap + frameW + Gap + legend;
            var sheet = new MapRect((float)Math.Floor((w - innerW - SheetPad * 2f) / 2f),
                (float)Math.Max(ChromeClear, Math.Floor(ChromeClear + (h - ChromeClear - Pad - innerH - SheetPad * 2f) / 2f)),
                innerW + SheetPad * 2f, innerH + SheetPad * 2f);
            var header = new MapRect(sheet.X + SheetPad, sheet.Y + SheetPad, innerW, TitleHeight);
            float rowY = header.Bottom + Gap;
            var left2 = new MapRect(header.X, rowY, col, frameW + Gap + legend);
            var frame2 = new MapRect(left2.Right + Gap, rowY, frameW, frameW);
            var legend2 = new MapRect(frame2.X, frame2.Bottom + Gap, frameW, legend);
            var right2 = new MapRect(frame2.Right + Gap, rowY, col, frameW + Gap + legend);
            return new MapScreenLayout(MapScreenMode.Tab, false, header, frame2, legend2, left2, right2, new MapRect(header.X, header.Y, innerW, innerH), sheet, header);
        }

        static MapScreenLayout Planning(float w, float h)
        {
            if (w < StackBelow)
            {
                float avail = w - Pad * 2f;
                float side = (float)Math.Floor(Math.Min(avail - FramePad * 2f, .46f * h));
                side = Clamp(side, 120f, 520f);
                float fw = side + FramePad * 2f;
                float x = (float)Math.Floor((w - fw) / 2f);
                float y = ChromeClear;
                var title = new MapRect(x, y, fw, PlanningTitleHeight);
                var frame = new MapRect(x, title.Bottom + Gap / 2f, fw, fw);
                var note = new MapRect(x, frame.Bottom + Gap / 2f, fw, PlanningNoteHeight);
                float sideTop = note.Bottom + Gap;
                float railWidth = Math.Max(0f, avail - Gap);
                float questWidth = Math.Min(PlanningQuestColumn, (float)Math.Floor(railWidth / 3f));
                float railHeight = Math.Max(80f, h - Pad - sideTop);
                var quests = new MapRect(Pad, sideTop, questWidth, railHeight);
                var decisions = new MapRect(quests.Right + Gap, sideTop, railWidth - questWidth, railHeight);
                return new MapScreenLayout(MapScreenMode.Planning, true, title, frame, note, quests, decisions,
                    new MapRect(Pad, y, avail, h - y - Pad));
            }
            float fixedH = PlanningTitleHeight + Gap + Gap + PlanningNoteHeight + FramePad * 2f;
            float sideMax = (float)Math.Floor(Math.Min(h - ChromeClear - Pad - fixedH,
                w - PlanningQuestColumn - PlanningColumn - Pad * 2f - Gap * 2f - FramePad * 2f));
            float mapSide = Clamp(sideMax, 260f, 780f);
            float frameW = mapSide + FramePad * 2f;
            float totalW = PlanningQuestColumn + Gap + frameW + Gap + PlanningColumn;
            float colH = PlanningTitleHeight + Gap + frameW + Gap + PlanningNoteHeight;
            float bx = (float)Math.Floor((w - totalW) / 2f);
            float by = (float)Math.Max(ChromeClear, Math.Floor((h - colH) / 2f + ChromeClear / 4f));
            var left2 = new MapRect(bx, by, PlanningQuestColumn, colH);
            float mapX = left2.Right + Gap;
            var title2 = new MapRect(mapX, by, frameW, PlanningTitleHeight);
            var frame2 = new MapRect(mapX, by + PlanningTitleHeight + Gap, frameW, frameW);
            var note2 = new MapRect(mapX, frame2.Bottom + Gap, frameW, PlanningNoteHeight);
            var column2 = new MapRect(frame2.Right + Gap, by, PlanningColumn, colH);
            return new MapScreenLayout(MapScreenMode.Planning, false, title2, frame2, note2, left2, column2, new MapRect(bx, by, totalW, colH));
        }
    }
}
