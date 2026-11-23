namespace Roguehalla.Net.Ui
{
    /// <summary>The anchor of a HUD region; same names and order as the Unity side's HudAnchor.</summary>
    public enum HudRegionAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    /// <summary>An absolute rectangle on the stage: x from the left edge, y from the TOP edge, HUD units.</summary>
    public readonly struct HudStageRect
    {
        public readonly float Left, Top, Width, Height;

        public HudStageRect(float left, float top, float width, float height) { Left = left; Top = top; Width = width; Height = height; }

        public float Right => 0f;
        public float Bottom => 0f;

        /// <summary>True when the interiors intersect; touching edges do not overlap.</summary>
        public bool Overlaps(HudStageRect other) => false;

        /// <summary>True when the whole rectangle lies on the stage.</summary>
        public bool Inside(float stageWidth, float stageHeight) => false;
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
        public HudStageRect On(float stageWidth, float stageHeight) => default;
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
        public static HudStageRect CombatZone(float stageWidth, float stageHeight) => default;

        public static HudRegion Consumables() => default;

        public static HudRegion AbilityRow() => default;

        /// <summary>Element i of the ability row, left to right: 0 weapon 1, 1 swap, 2 weapon 2, 3 R, 4 Shift, 5 Q.</summary>
        public static HudRegion AbilityElement(int index) => default;

        public static HudRegion Prompt(float width = PromptWidth) => default;

        /// <summary>min(wanted, stageWidth - 2 * Margin).</summary>
        public static float LaneWidth(float stageWidth, float wanted = LaneMaxWidth) => 0f;

        /// <summary>Feedback pill i of the bottom-centre lane; index 0 is the lowest.</summary>
        public static HudRegion FeedbackPill(int index, float width) => default;

        /// <summary>Pill i of the event lane, index 0 on top; the lane starts TopCentreGap below the lowest top-centre pill, never above EventTop.</summary>
        public static HudRegion EventPill(int index, float width, float topCentreBottom = EventTop - TopCentreGap) => default;

        /// <summary>The quest tracker under the top-left plate chain; rows 0 gives a region of height 0.</summary>
        public static HudRegion TrackerDock(float plateChainBottom, int rows) => default;

        /// <summary>The quest offer card on the left, centred when that clears the tracker, otherwise just below it.</summary>
        public static HudRegion OfferCard(float stageHeight, float plateChainBottom, int trackerRows, float wantedHeight, float championTop) => default;
    }
}
