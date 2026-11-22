using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>A sheet of individually painted square stones laid in aligned map-space courses.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Flagstone floor", fileName = "FlagstoneFloor")]
    public sealed class FlagstoneFloorArt : RegionalFloorArt
    {
        [Range(2, 8), Tooltip("Number of stone faces along one edge of the square sheet. Repeat Span is the footprint of the full sheet, in tiles.")]
        public int StonesPerSide = 4;
    }
}
