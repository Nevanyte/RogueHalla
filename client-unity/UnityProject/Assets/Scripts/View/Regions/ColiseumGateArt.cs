using UnityEngine;

namespace Roguehalla.Client
{
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Coliseum gate", fileName = "ColiseumGatePierArt")]
    public sealed class ColiseumGateArt : StormInkPropArtDefinition
    {
        [Header("Closed gate")]
        [Min(.02f), Tooltip("Width of each painted iron upright, in world units.")]
        public float GateBarWidthWorldUnits = .055f;
        [Min(.3f), Tooltip("Height of the closed gate bars above the walkable tile, in world units.")]
        public float GateBarHeightWorldUnits = 1.18f;
        [ColorUsage(false, false), Tooltip("Ink-dark iron used by the closed gate bars.")]
        public Color GateIron = new Color(.10f, .12f, .15f, 1f);
        [ColorUsage(false, false), Tooltip("Cool edge pigment on the closed gate bars.")]
        public Color GateIronHighlight = new Color(.48f, .52f, .55f, 1f);

        [Range(.12f, .44f), Tooltip("Thickness of the forged frame seen from above at the east and west gates, in world units.")]
        public float FrameDepthWorldUnits = .31f;
    }
}
