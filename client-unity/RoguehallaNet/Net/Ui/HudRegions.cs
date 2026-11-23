namespace Roguehalla.Net.Ui
{
    /// <summary>The anchor of a HUD region; same names and order as the Unity side's HudAnchor.</summary>
    public enum HudRegionAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    /// <summary>An absolute rectangle on the stage: x from the left edge, y from the TOP edge, HUD units.</summary>
    public readonly struct HudStageRect
    {
        public readonly float Left, Top, Width, Height;

        public HudStageRect(float left, float top, float width, float height) { Left = left; Top = top; Width = width; Height = height; }

        public float Right => Left + Width;
        public float Bottom => Top + Height;

        /// <summary>True when the interiors intersect; touching edges do not overlap.</summary>
        public bool Overlaps(HudStageRect other) =>
            Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

        /// <summary>True when the whole rectangle lies on the stage.</summary>
        public bool Inside(float stageWidth, float stageHeight) =>
            Left >= 0f && Top >= 0f && Right <= stageWidth && Bottom <= stageHeight;
    }

    /// <summary>
    /// One HUD region as HudUi.Box takes it: for a left or right anchor X is the inset from that edge, for top or bottom Y is the
    /// inset from that edge, for a centred axis the value is a signed offset (right and up positive).
    /// </summary>
    public readonly struct HudRegion
    {
        public readonly HudRegionAnchor Anchor;
        public readonly float X, Y, Width, Height;

        public HudRegion(HudRegionAnchor anchor, float x, float y, float width, float height) { Anchor = anchor; X = x; Y = y; Width = width; Height = height; }

        /// <summary>The region's rectangle on a stage of the given size.</summary>
        public HudStageRect On(float stageWidth, float stageHeight)
        {
            int a = (int)Anchor;
            int col = a % 3, row = a / 3;
            float left = col == 0 ? X : col == 1 ? stageWidth * .5f + X - Width * .5f : stageWidth - X - Width;
            float top = row == 0 ? Y : row == 1 ? stageHeight * .5f - Y - Height * .5f : stageHeight - Y - Height;
            return new HudStageRect(left, top, Width, Height);
        }
    }

    /// <summary>Where each top-level in-match HUD region sits. The single source of these numbers.</summary>
    public static class HudRegions
    {
        public const float Margin = 12f;
        // centre bottom, from the bottom edge up
        public const float ConsumablesBottom = 14f, ConsumablesHeight = 28f, ConsumablesWidth = 352f;
        public const float AbilityBottom = 48f, AbilityHeight = 76f, WeaponSlotWidth = 104f, SwapWidth = 28f, TileSize = 64f, AbilityGap = 6f;
        public const float AbilityWidth = 2f * WeaponSlotWidth + SwapWidth + 3f * TileSize + 5f * AbilityGap;
        public const float PromptBottom = 130f, PromptHeight = 26f, PromptWidth = 300f, RewardDockWidth = 420f;
        public const float FeedbackBottom = 162f;
        public const int FeedbackMax = 2;
        // centre top
        public const float EventTop = 46f;
        public const int EventMax = 3;
        public const float PillHeight = 24f, PillGap = 4f, LaneMaxWidth = 460f, TopCentreGap = 8f;
        // left column
        public const float TrackerWidth = 260f, TrackerHeader = 18f, TrackerRow = 38f, TrackerGap = 8f;
        public const int TrackerMaxRows = 3;
        public const float OfferWidth = 280f, OfferMinHeight = 96f;
        // the middle of the screen that every region keeps out of
        public const float CombatZoneWidthShare = .46f, CombatZoneHeightShare = .44f;

        /// <summary>The centred middle of the stage that no region may cover.</summary>
        public static HudStageRect CombatZone(float stageWidth, float stageHeight)
        {
            float w = stageWidth * CombatZoneWidthShare, h = stageHeight * CombatZoneHeightShare;
            return new HudStageRect((stageWidth - w) * .5f, (stageHeight - h) * .5f, w, h);
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        public static HudRegion Consumables() =>
            new HudRegion(HudRegionAnchor.BottomCenter, 0f, ConsumablesBottom, ConsumablesWidth, ConsumablesHeight);

        public static HudRegion AbilityRow() =>
            new HudRegion(HudRegionAnchor.BottomCenter, 0f, AbilityBottom, AbilityWidth, AbilityHeight);

        /// <summary>Element i of the ability row, left to right: 0 weapon 1, 1 swap, 2 weapon 2, 3 R, 4 Shift, 5 Q.</summary>
        public static HudRegion AbilityElement(int index)
        {
            index = Clamp(index, 0, 5);
            const float slotStep = WeaponSlotWidth + AbilityGap, swapStep = SwapWidth + AbilityGap, tileStep = TileSize + AbilityGap;
            switch (index)
            {
                case 0: return new HudRegion(HudRegionAnchor.BottomLeft, 0f, 0f, WeaponSlotWidth, AbilityHeight);
                case 1: return new HudRegion(HudRegionAnchor.BottomLeft, slotStep, 0f, SwapWidth, 40f);
                case 2: return new HudRegion(HudRegionAnchor.BottomLeft, slotStep + swapStep, 0f, WeaponSlotWidth, AbilityHeight);
                default:
                    return new HudRegion(HudRegionAnchor.BottomLeft, 2f * slotStep + swapStep + (index - 3) * tileStep, 0f, TileSize, TileSize);
            }
        }

        public static HudRegion Prompt(float width = PromptWidth) =>
            new HudRegion(HudRegionAnchor.BottomCenter, 0f, PromptBottom, width, PromptHeight);

        /// <summary>The scale of the action row on a narrow stage: 1 while the centred row clears, by 8 on each side, a
        /// panel that ends leftObstacleRight from the left edge (the champion panel) and one that starts rightObstacleLeft from the
        /// left edge (the minimap); smaller below that; never under .6.</summary>
        public static float AbilityRowScale(float stageWidth, float leftObstacleRight, float rightObstacleLeft)
        {
            return 1f;   // stub: P-UI-ABILITY-ROW-1 implements this
        }

        public const float PromptPairWidth = 220f;

        /// <summary>The prompt lane when it holds two pills: index 0 the left one, 1 the right one, 4 from the centre line.</summary>
        public static HudRegion PromptPair(int index)
        {
            return Prompt(PromptPairWidth);   // stub: P-UI-ABILITY-ROW-1 implements this
        }

        /// <summary>min(wanted, stageWidth - 2 * Margin).</summary>
        public static float LaneWidth(float stageWidth, float wanted = LaneMaxWidth)
        {
            float room = stageWidth - 2f * Margin;
            return wanted < room ? wanted : room;
        }

        /// <summary>Feedback pill i of the bottom-centre lane; index 0 is the lowest.</summary>
        public static HudRegion FeedbackPill(int index, float width) =>
            new HudRegion(HudRegionAnchor.BottomCenter, 0f, FeedbackBottom + Clamp(index, 0, FeedbackMax - 1) * (PillHeight + PillGap), width, PillHeight);

        /// <summary>Pill i of the event lane, index 0 on top; the lane starts TopCentreGap below the lowest top-centre pill, never above EventTop.</summary>
        public static HudRegion EventPill(int index, float width, float topCentreBottom = EventTop - TopCentreGap)
        {
            float start = topCentreBottom + TopCentreGap;
            if (start < EventTop) start = EventTop;
            return new HudRegion(HudRegionAnchor.TopCenter, 0f, start + Clamp(index, 0, EventMax - 1) * (PillHeight + PillGap), width, PillHeight);
        }

        /// <summary>The quest tracker under the top-left plate chain; rows 0 gives a region of height 0.</summary>
        public static HudRegion TrackerDock(float plateChainBottom, int rows)
        {
            rows = Clamp(rows, 0, TrackerMaxRows);
            float height = rows == 0 ? 0f : TrackerHeader + rows * TrackerRow;
            return new HudRegion(HudRegionAnchor.TopLeft, Margin, plateChainBottom, TrackerWidth, height);
        }

        /// <summary>The quest offer card on the left, centred when that clears the tracker, otherwise just below it.</summary>
        public static HudRegion OfferCard(float stageHeight, float plateChainBottom, int trackerRows, float wantedHeight, float championTop)
        {
            var tracker = TrackerDock(plateChainBottom, trackerRows);
            float clear = tracker.Y + tracker.Height;
            float centred = (stageHeight - wantedHeight) * .5f;
            float top = centred >= clear ? centred : clear;
            float height = championTop - TrackerGap - top;
            if (height > wantedHeight) height = wantedHeight;
            if (height < OfferMinHeight) height = OfferMinHeight;
            return new HudRegion(HudRegionAnchor.TopLeft, Margin, top, OfferWidth, height);
        }
    }
}
